using System.Data;

namespace Apperture.ModuleSdk.Testing;

/// <summary>Minimal no-op <see cref="IDbConnection"/> for harness step tests.</summary>
internal sealed class HarnessStubDbConnection : IDbConnection
{
    public string? ConnectionString { get; set; } = "";

    public int ConnectionTimeout => 0;

    public string Database => "harness";

    public ConnectionState State { get; private set; } = ConnectionState.Closed;

    public IDbTransaction BeginTransaction() => throw new NotSupportedException();

    public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotSupportedException();

    public void ChangeDatabase(string databaseName) => throw new NotSupportedException();

    public void Close() => State = ConnectionState.Closed;

    public IDbCommand CreateCommand() => throw new NotSupportedException();

    public void Open() => State = ConnectionState.Open;

    public void Dispose() => Close();
}
