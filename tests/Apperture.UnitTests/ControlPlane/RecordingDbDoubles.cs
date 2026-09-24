using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Minimal ADO.NET doubles that record exactly what a caller sends: command text, parameters, timeout, and disposal. They can
/// only ever run a query for a reader; scalar and non-query execution THROW, so a test fails loudly if anything ever tries to
/// run a statement any other way.
/// </summary>
internal sealed class RecordingDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;

    public List<RecordingDbCommand> Commands { get; } = [];
    public int OpenCount { get; private set; }
    public bool Disposed { get; private set; }
    public Func<RecordingDbCommand, DataTable> Rows { get; set; } = _ => new DataTable();
    public Func<CancellationToken, Task>? OnOpen { get; set; }
    public Func<RecordingDbCommand, CancellationToken, Task>? OnExecute { get; set; }
    public Exception? OpenFailure { get; set; }

    [AllowNull]
    public override string ConnectionString { get; set; } = "";
    public override string Database => "recorded";
    public override string DataSource => "recorded";
    public override string ServerVersion => "0";
    public override ConnectionState State => _state;

    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
    public override void Close() => _state = ConnectionState.Closed;
    public override void Open() => throw new NotSupportedException("Synchronous open is not used.");

    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        OpenCount++;
        cancellationToken.ThrowIfCancellationRequested();
        if (OnOpen is not null)
            await OnOpen(cancellationToken).ConfigureAwait(false);
        if (OpenFailure is not null)
            throw OpenFailure;
        _state = ConnectionState.Open;
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        throw new NotSupportedException("A structure request never opens a transaction.");

    protected override DbCommand CreateDbCommand()
    {
        var command = new RecordingDbCommand(this);
        Commands.Add(command);
        return command;
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        _state = ConnectionState.Closed;
        base.Dispose(disposing);
    }
}

internal sealed class RecordingDbCommand(RecordingDbConnection owner) : DbCommand
{
    public bool Disposed { get; private set; }
    public bool Executed { get; private set; }
    public DataTableReader? Reader { get; private set; }
    public CommandBehavior? Behavior { get; private set; }
    public CancellationToken TokenSeen { get; private set; }

    [AllowNull]
    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; } = 30;
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; } = owner;
    protected override DbParameterCollection DbParameterCollection { get; } = new RecordingParameters();
    protected override DbTransaction? DbTransaction { get; set; }

    public IReadOnlyList<RecordingParameter> BoundParameters => ((RecordingParameters)DbParameterCollection).Items;

    public override void Cancel() { }
    public override int ExecuteNonQuery() => throw new InvalidOperationException("A structure request must never run a non-query.");
    public override object? ExecuteScalar() => throw new InvalidOperationException("A structure request must never run a scalar.");
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new RecordingParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        throw new NotSupportedException("Synchronous execution is not used.");

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        Executed = true;
        Behavior = behavior;
        TokenSeen = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        if (owner.OnExecute is not null)
            await owner.OnExecute(this, cancellationToken).ConfigureAwait(false);
        Reader = new DataTableReader(owner.Rows(this));
        return Reader;
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

internal sealed class RecordingParameter : DbParameter
{
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
    public override bool IsNullable { get; set; }
    [AllowNull]
    public override string ParameterName { get; set; } = "";
    public override int Size { get; set; }
    [AllowNull]
    public override string SourceColumn { get; set; } = "";
    public override bool SourceColumnNullMapping { get; set; }
    public override object? Value { get; set; }
    public override void ResetDbType() { }
}

internal sealed class RecordingParameters : DbParameterCollection
{
    public List<RecordingParameter> Items { get; } = [];

    public override int Count => Items.Count;
    public override object SyncRoot => Items;

    public override int Add(object value)
    {
        Items.Add((RecordingParameter)value);
        return Items.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var value in values)
            Add(value!);
    }

    public override void Clear() => Items.Clear();
    public override bool Contains(object value) => Items.Contains((RecordingParameter)value);
    public override bool Contains(string value) => Items.Any(p => p.ParameterName == value);
    public override void CopyTo(Array array, int index) => ((ICollection)Items).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => Items.GetEnumerator();
    public override int IndexOf(object value) => Items.IndexOf((RecordingParameter)value);
    public override int IndexOf(string parameterName) => Items.FindIndex(p => p.ParameterName == parameterName);
    public override void Insert(int index, object value) => Items.Insert(index, (RecordingParameter)value);
    public override void Remove(object value) => Items.Remove((RecordingParameter)value);
    public override void RemoveAt(int index) => Items.RemoveAt(index);
    public override void RemoveAt(string parameterName) => Items.RemoveAt(IndexOf(parameterName));
    protected override DbParameter GetParameter(int index) => Items[index];
    protected override DbParameter GetParameter(string parameterName) => Items[IndexOf(parameterName)];
    protected override void SetParameter(int index, DbParameter value) => Items[index] = (RecordingParameter)value;
    protected override void SetParameter(string parameterName, DbParameter value) => Items[IndexOf(parameterName)] = (RecordingParameter)value;
}
