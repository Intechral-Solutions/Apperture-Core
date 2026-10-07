using Microsoft.Data.SqlClient;

namespace Apperture.Persistence.SqlServer.Audit;

/// <summary>Bounds of the audited unit of work (AUD-1 design gate §13.5, I-3).</summary>
public sealed class AuditRecorderOptions
{
    /// <summary>How long an attempt waits for its own operation lock before giving up.</summary>
    public TimeSpan OperationLockWait { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long verification waits for the operation lock. Not granted in time: outcome unknown.</summary>
    public TimeSpan VerificationLockWait { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Reruns allowed after a verified rollback, before the operation is reported as not completed.</summary>
    public int MaxRerunsAfterConfirmedRollback { get; set; } = 2;
}

/// <summary>Generates event ids. A test seam: the default is a fresh 32-hex GUID.</summary>
internal interface IAuditEventIdSource
{
    string NewEventId();
}

internal sealed class GuidAuditEventIdSource : IAuditEventIdSource
{
    public static GuidAuditEventIdSource Instance { get; } = new();

    public string NewEventId() => Guid.NewGuid().ToString("N");
}

/// <summary>
/// Opens the fresh connection verification uses: the same authoritative database, never a cached context. A test seam
/// for a verification connection that fails.
/// </summary>
internal interface IAuditVerificationConnectionFactory
{
    Task<SqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken);
}

internal sealed class SqlAuditVerificationConnectionFactory : IAuditVerificationConnectionFactory
{
    public static SqlAuditVerificationConnectionFactory Instance { get; } = new();

    public async Task<SqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        // Pooling off: a pooled connection could be the very session whose fate is being established.
        var builder = new SqlConnectionStringBuilder(connectionString) { Pooling = false };
        var connection = new SqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
