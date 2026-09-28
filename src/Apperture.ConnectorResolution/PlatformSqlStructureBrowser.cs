using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Net.Sockets;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Abstractions;
using Microsoft.Data.SqlClient;

namespace Apperture.ConnectorResolution;

/// <summary>Fixed bounds for every structure request. The defaults are the product's limits; tests may tighten them.</summary>
public sealed class SqlStructureBrowserOptions
{
    /// <summary>The most items any one request returns. One more is fetched to know whether the list was truncated.</summary>
    public int MaxItems { get; init; } = 500;

    /// <summary>Explicit per-command timeout, in seconds. The verifier's probe uses the same value.</summary>
    public int CommandTimeoutSeconds { get; init; } = 15;

    /// <summary>Explicit connect timeout, in seconds.</summary>
    public int ConnectTimeoutSeconds { get; init; } = 15;
}

/// <summary>
/// Browses the STRUCTURE of the database a platform SQL Connection points at: schemas, then tables and views, then columns.
/// It issues only the fixed statements in <see cref="SqlStructureQueries"/>, with every selection bound as a parameter
/// VALUE. It reads no rows, runs no operator SQL, changes nothing, and builds no dynamic SQL.
/// <para>
/// A remote client exists only for the duration of one operator-invoked request: created inside the call, never during page
/// or list rendering or provider lookup, never cached or pooled by the platform, and always disposed. Every request has an
/// explicit connect and command timeout, an item cap, and threads the caller's cancellation token through open, execute and
/// read. Failures are reported as fixed, safe outcomes; remote error text, connection strings and secrets never leave here.
/// </para>
/// </summary>
public sealed class PlatformSqlStructureBrowser : ISqlStructureBrowser
{
    private const string ApplicationName = "Apperture structure browser";

    private const string DeniedMessage =
        "The database refused this request for the connection's login. What structure is visible depends on that login's permissions.";

    private const string UnavailableMessage = "The database could not be reached, or did not answer in time.";
    private const string IncompleteMessage = "The connection's stored settings are incomplete.";

    private const string SqlLoginRequiredMessage =
        "Browsing structure needs a connection that signs in with its own SQL login (a User ID). Connections that use the platform's own identity cannot be browsed.";
    private const string FailedMessage = "The structure request could not be completed.";

    private readonly ISecretResolver _secretResolver;
    private readonly IConnectorClientFactory<IDbConnection> _factory;
    private readonly SqlStructureBrowserOptions _options;

    public PlatformSqlStructureBrowser(
        ISecretResolver secretResolver,
        IConnectorClientFactory<IDbConnection> factory,
        SqlStructureBrowserOptions? options = null)
    {
        _secretResolver = secretResolver;
        _factory = factory;
        _options = options ?? new SqlStructureBrowserOptions();
    }

    /// <inheritdoc />
    public ConnectorDefinitionId ConnectorDefinitionId => WellKnownConnectorDefinitionIds.PlatformSql;

    /// <inheritdoc />
    public ConnectionToolAvailability Availability => ConnectionToolAvailability.Available;

    /// <inheritdoc />
    public ValueTask<SqlStructureResult<SqlSchemaInfo>> ListSchemasAsync(Connection connection, CancellationToken cancellationToken) =>
        RunAsync(
            connection, SqlStructureQueries.Schemas, schema: null, table: null,
            static r => new SqlSchemaInfo(r.GetString(0)),
            cancellationToken);

    /// <inheritdoc />
    public ValueTask<SqlStructureResult<SqlTableInfo>> ListTablesAsync(Connection connection, string schema, CancellationToken cancellationToken)
    {
        ValidateIdentifier(schema, nameof(schema));
        return RunAsync(
            connection, SqlStructureQueries.Tables, schema, table: null,
            static r => new SqlTableInfo(r.GetString(0), r.GetString(1), string.Equals(r.GetString(2), "VIEW", StringComparison.OrdinalIgnoreCase)),
            cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<SqlStructureResult<SqlColumnInfo>> ListColumnsAsync(
        Connection connection, string schema, string table, CancellationToken cancellationToken)
    {
        ValidateIdentifier(schema, nameof(schema));
        ValidateIdentifier(table, nameof(table));
        return RunAsync(
            connection, SqlStructureQueries.Columns, schema, table,
            static r => new SqlColumnInfo(
                r.GetString(0),
                r.GetString(1),
                string.Equals(r.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                r.GetInt32(3),
                r.IsDBNull(4) ? null : r.GetInt32(4)),
            cancellationToken);
    }

    /// <summary>Length and emptiness only. Content is irrelevant: the value is only ever bound as a parameter.</summary>
    private static void ValidateIdentifier(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A name is required.", name);
        if (value.Length > SqlStructureQueries.MaxIdentifierLength)
            throw new ArgumentException($"A name is at most {SqlStructureQueries.MaxIdentifierLength} characters.", name);
    }

    private async ValueTask<SqlStructureResult<T>> RunAsync<T>(
        Connection connection,
        string commandText,
        string? schema,
        string? table,
        Func<IDataRecord, T> map,
        CancellationToken cancellationToken)
    {
        var limit = _options.MaxItems;

        // The client is created only now, because an operator invoked this request.
        ResolvedConnectionMaterial material;
        try
        {
            material = await ResolvedConnectionMaterialBuilder.BuildAsync(connection, _secretResolver, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Includes a resolver's own timeout, which is a TaskCanceledException the caller did not ask for.
            return Failure<T>(ConnectionToolOutcome.SecretsUnavailable, ConnectionVerificationMessages.SecretResolutionFailed, limit);
        }

        // The remote login's own rights are the boundary of what this tool can show. A Connection with no User ID signs in
        // with the platform host's own Windows identity, which is not "the connection's login" and is usually far more
        // privileged; browsing it would let an operator enumerate the platform's own databases. Refused before any client exists.
        if (!material.ConfigAndResolvedSecrets.TryGetValue("UserId", out var userId) || string.IsNullOrWhiteSpace(userId))
            return Failure<T>(ConnectionToolOutcome.Failed, SqlLoginRequiredMessage, limit);

        IDbConnection raw;
        try
        {
            raw = _factory.Create(connection, material);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure<T>(ConnectionToolOutcome.Failed, IncompleteMessage, limit);
        }

        if (raw is not DbConnection dbConnection)
        {
            raw.Dispose();
            return Failure<T>(ConnectionToolOutcome.Failed, FailedMessage, limit);
        }

        try
        {
            await using (dbConnection.ConfigureAwait(false))
            {
                Bound(dbConnection);
                await dbConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

                await using var command = dbConnection.CreateCommand();
                command.CommandText = commandText;
                command.CommandType = CommandType.Text;
                command.CommandTimeout = _options.CommandTimeoutSeconds;
                AddParameter(command, SqlStructureQueries.LimitParameter, DbType.Int32, limit + 1);
                if (schema is not null)
                    AddParameter(command, SqlStructureQueries.SchemaParameter, DbType.String, schema);
                if (table is not null)
                    AddParameter(command, SqlStructureQueries.TableParameter, DbType.String, table);

                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

                var items = new List<T>();
                var truncated = false;
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (items.Count == limit)
                    {
                        truncated = true;
                        break;
                    }

                    items.Add(map(reader));
                }

                return new SqlStructureResult<T>(ConnectionToolOutcome.Succeeded, items, truncated, limit);
            }
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested)
        {
            // Some providers report a caller's cancellation as a provider error; it is still the caller's cancellation.
            if (ex is OperationCanceledException)
                throw;
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception ex)
        {
            var (outcome, message) = Classify(ex);
            return Failure<T>(outcome, message, limit);
        }
    }

    /// <summary>Explicit, deterministic transport settings. Pooling is off so the physical connection closes with the request.</summary>
    internal void Bound(DbConnection connection)
    {
        if (connection is not SqlConnection sql)
            return;

        var builder = new SqlConnectionStringBuilder(sql.ConnectionString)
        {
            ConnectTimeout = _options.ConnectTimeoutSeconds,
            ApplicationName = ApplicationName,
            Pooling = false
        };
        sql.ConnectionString = builder.ConnectionString;
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        if (type == DbType.String)
            parameter.Size = SqlStructureQueries.MaxIdentifierLength;
        command.Parameters.Add(parameter);
    }

    private static SqlStructureResult<T> Failure<T>(ConnectionToolOutcome outcome, string message, int limit) =>
        new(outcome, [], Truncated: false, limit, message);

    internal static (ConnectionToolOutcome Outcome, string Message) Classify(Exception exception)
    {
        // Microsoft.Data.SqlClient reports the same closed-port failure as a Win32/SQL number on Windows and as
        // Number = 0 wrapping a SocketException on Linux. Walk the chain; do not stop at an unclassified SqlException.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql)
            {
                foreach (SqlError error in sql.Errors)
                {
                    var fromError = ClassifySqlNumber(error.Number);
                    if (fromError != ConnectionToolOutcome.Failed)
                        return (fromError, MessageFor(fromError));
                }

                var fromNumber = ClassifySqlNumber(sql.Number);
                if (fromNumber != ConnectionToolOutcome.Failed)
                    return (fromNumber, MessageFor(fromNumber));
            }
            else if (current is TimeoutException)
            {
                return (ConnectionToolOutcome.RemoteUnavailable, UnavailableMessage);
            }
            else if (current is OperationCanceledException)
            {
                // Caller cancellation is filtered before Classify. A leftover cancel is the provider's own connect timeout.
                return (ConnectionToolOutcome.RemoteUnavailable, UnavailableMessage);
            }
            else if (current is SocketException socket && IsRemoteUnavailableSocket(socket.SocketErrorCode))
            {
                return (ConnectionToolOutcome.RemoteUnavailable, UnavailableMessage);
            }
            else if (current is Win32Exception win32
                     && ClassifySqlNumber(win32.NativeErrorCode) == ConnectionToolOutcome.RemoteUnavailable)
            {
                return (ConnectionToolOutcome.RemoteUnavailable, UnavailableMessage);
            }
        }

        return (ConnectionToolOutcome.Failed, FailedMessage);
    }

    /// <summary>
    /// The remote system's own answer: a refusal is <see cref="ConnectionToolOutcome.RemoteDenied"/>, an unreachable or slow
    /// server is <see cref="ConnectionToolOutcome.RemoteUnavailable"/>. Anything unrecognised is a generic failure.
    /// </summary>
    internal static ConnectionToolOutcome ClassifySqlNumber(int number) => number switch
    {
        // Login failed; cannot open the database for this login; permission denied.
        18456 or 18452 or 4060 or 916 or 229 or 230 or 262 or 297 or 300 => ConnectionToolOutcome.RemoteDenied,

        // Timeout expired, general network errors, server not found or not listening, connection dropped.
        // 111/110/104/101/113 are POSIX TCP errno values SqlClient may copy onto Number on Linux.
        -2 or -1 or 2 or 20 or 26 or 40 or 53 or 64 or 121 or 233 or 258
            or 111 or 110 or 104 or 101 or 113
            or 1225 or 1226 or 1231 or 1232 or 10051 or 10053 or 10054 or 10060 or 10061 or 10065 or 11001
            => ConnectionToolOutcome.RemoteUnavailable,

        _ => ConnectionToolOutcome.Failed
    };

    private static bool IsRemoteUnavailableSocket(SocketError error) => error is
        SocketError.ConnectionRefused
        or SocketError.TimedOut
        or SocketError.HostNotFound
        or SocketError.TryAgain
        or SocketError.NetworkUnreachable
        or SocketError.HostUnreachable
        or SocketError.NetworkDown
        or SocketError.AddressNotAvailable
        or SocketError.ConnectionReset
        or SocketError.ConnectionAborted
        or SocketError.Shutdown
        or SocketError.NotConnected;

    private static string MessageFor(ConnectionToolOutcome outcome) => outcome switch
    {
        ConnectionToolOutcome.RemoteDenied => DeniedMessage,
        ConnectionToolOutcome.RemoteUnavailable => UnavailableMessage,
        _ => FailedMessage
    };
}
