using System.Data;
using System.Data.Common;
using Apperture.Core.Application.AdministratorViability;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Apperture.Persistence.SqlServer;

/// <inheritdoc cref="IHostConfigurationAttestationStore" />
/// <remarks>
/// <para>Every statement stamps its times with the server's <c>SYSUTCDATETIME()</c> and computes the database binding on the server,
/// so a writer and a reader on different machines, with different clocks and differently spelled connection strings, agree.
/// Nothing a host sends decides a time or the binding.</para>
/// <para>A process owns its row by <c>ProcessInstanceId</c>. Claiming replaces whatever an earlier process of the same host wrote
/// (the new process wins); refreshing and stopping are conditional on still owning the row, so an older process that is still
/// running can neither overwrite nor stop the process that replaced it. Taking a row back (<c>ReclaimAsync</c>) is decided by the
/// same statement that writes it, against the database's clock and the shared freshness window: a live holder is never displaced.
/// Refreshing re-stamps the binding together with the time, so the stored binding is always the one the write arrived in.</para>
/// <para>The statements run on the context's connection and join an open transaction when there is one, so a reader inside an
/// audited unit reads under the unit's own isolation.</para>
/// </remarks>
public sealed class SqlHostConfigurationAttestationStore : IHostConfigurationAttestationStore
{
    /// <summary>
    /// The binding of a database: a hash of the server's name and the database's name, computed by the server itself. It binds a
    /// reading to the database it was written in, so a copy restored elsewhere, or a command line pointed at another database,
    /// does not match. It is a guard against operator error, not a cryptographic identity of a database.
    /// </summary>
    internal const string FingerprintSql =
        "CONVERT(nvarchar(64), HASHBYTES('SHA2_256', CONCAT(CONVERT(nvarchar(256), SERVERPROPERTY('ServerName')), N'|', DB_NAME())), 2)";

    private const string Table = "[HostConfigurationAttestations]";

    private readonly AppertureDbContext _db;

    public SqlHostConfigurationAttestationStore(AppertureDbContext db) => _db = db;

    /// <inheritdoc />
    public async ValueTask ClaimAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default)
    {
        Validate(write);
        await RunAsync(
            $"""
            DECLARE @now datetime2 = SYSUTCDATETIME();
            DECLARE @fingerprint nvarchar(64) = {FingerprintSql};
            UPDATE {Table}
               SET [Source] = @source, [SchemaVersion] = @schemaVersion, [ProcessInstanceId] = @process,
                   [ProcessStartedUtc] = @started, [ClaimedUtc] = @now, [RefreshedUtc] = @now, [StoppedUtc] = NULL,
                   [DatabaseFingerprint] = @fingerprint, [Payload] = @payload
             WHERE [HostId] = @host;
            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO {Table}
                    ([HostId], [Source], [SchemaVersion], [ProcessInstanceId], [ProcessStartedUtc], [ClaimedUtc], [RefreshedUtc],
                     [StoppedUtc], [DatabaseFingerprint], [Payload])
                SELECT @host, @source, @schemaVersion, @process, @started, @now, @now, NULL, @fingerprint, @payload
                 WHERE NOT EXISTS (SELECT 1 FROM {Table} WITH (UPDLOCK, HOLDLOCK) WHERE [HostId] = @host);
                IF @@ROWCOUNT = 0
                    UPDATE {Table}
                       SET [Source] = @source, [SchemaVersion] = @schemaVersion, [ProcessInstanceId] = @process,
                           [ProcessStartedUtc] = @started, [ClaimedUtc] = @now, [RefreshedUtc] = @now, [StoppedUtc] = NULL,
                           [DatabaseFingerprint] = @fingerprint, [Payload] = @payload
                     WHERE [HostId] = @host;
            END
            """,
            command => AddWriteParameters(command, write),
            async command => await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<HostAttestationRefreshOutcome> RefreshAsync(
        HostConfigurationAttestationWrite write,
        CancellationToken cancellationToken = default)
    {
        Validate(write);
        // The binding is re-stamped with the time: the row an owning process keeps alive names the database this write arrived in,
        // computed by the server, never one the host names. Ownership (the process id, not stopped) is the only way in, so a process
        // cannot use a refresh to take a row from another process or to bind a row it does not own.
        var code = await RunAsync(
            $"""
            DECLARE @fingerprint nvarchar(64) = {FingerprintSql};
            UPDATE {Table}
               SET [RefreshedUtc] = SYSUTCDATETIME(), [SchemaVersion] = @schemaVersion, [DatabaseFingerprint] = @fingerprint,
                   [Payload] = @payload
             WHERE [HostId] = @host AND [ProcessInstanceId] = @process AND [StoppedUtc] IS NULL;
            IF @@ROWCOUNT > 0 SELECT 0;
            ELSE IF EXISTS (SELECT 1 FROM {Table} WHERE [HostId] = @host) SELECT 1;
            ELSE SELECT 2;
            """,
            command => AddWriteParameters(command, write),
            async command => Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);

        return code switch
        {
            0 => HostAttestationRefreshOutcome.Refreshed,
            1 => HostAttestationRefreshOutcome.Superseded,
            _ => HostAttestationRefreshOutcome.Missing,
        };
    }

    /// <inheritdoc />
    public async ValueTask<HostAttestationReclaimOutcome> ReclaimAsync(
        HostConfigurationAttestationWrite write,
        CancellationToken cancellationToken = default)
    {
        Validate(write);
        var window = HostConfigurationAttestationLiveness.FreshnessWindow;
        if (window.Ticks % TimeSpan.TicksPerSecond != 0)
            throw new InvalidOperationException("The freshness window must be a whole number of seconds.");

        // One statement decides and writes, so competing candidates are serialised by the row's own lock: the second to reach the row
        // finds the first's fresh, unstopped record and is refused. The row is taken when it is the caller's own (an idempotent retry:
        // the claim time and start time are kept), or when it is another process's and that record is stopped or strictly older than
        // the window, the same line the reader draws. A record dated after the database's clock is neither: it is held.
        var code = await RunAsync(
            $"""
            DECLARE @now datetime2 = SYSUTCDATETIME();
            DECLARE @fingerprint nvarchar(64) = {FingerprintSql};
            DECLARE @staleBefore datetime2 = DATEADD(SECOND, -@windowSeconds, @now);
            UPDATE {Table}
               SET [Source] = @source, [SchemaVersion] = @schemaVersion, [ProcessInstanceId] = @process,
                   [ProcessStartedUtc] = CASE WHEN [ProcessInstanceId] = @process THEN [ProcessStartedUtc] ELSE @started END,
                   [ClaimedUtc] = CASE WHEN [ProcessInstanceId] = @process THEN [ClaimedUtc] ELSE @now END,
                   [RefreshedUtc] = @now, [StoppedUtc] = NULL, [DatabaseFingerprint] = @fingerprint, [Payload] = @payload
             WHERE [HostId] = @host
               AND (([ProcessInstanceId] = @process AND [StoppedUtc] IS NULL)
                    OR ([ProcessInstanceId] <> @process AND ([StoppedUtc] IS NOT NULL OR [RefreshedUtc] < @staleBefore)));
            IF @@ROWCOUNT > 0
            BEGIN
                SELECT 0;
                RETURN;
            END;
            IF EXISTS (SELECT 1 FROM {Table} WHERE [HostId] = @host)
            BEGIN
                SELECT 1;
                RETURN;
            END;
            INSERT INTO {Table}
                ([HostId], [Source], [SchemaVersion], [ProcessInstanceId], [ProcessStartedUtc], [ClaimedUtc], [RefreshedUtc],
                 [StoppedUtc], [DatabaseFingerprint], [Payload])
            SELECT @host, @source, @schemaVersion, @process, @started, @now, @now, NULL, @fingerprint, @payload
             WHERE NOT EXISTS (SELECT 1 FROM {Table} WITH (UPDLOCK, HOLDLOCK) WHERE [HostId] = @host);
            IF @@ROWCOUNT > 0 SELECT 0; ELSE SELECT 1;
            """,
            command =>
            {
                AddWriteParameters(command, write);
                command.Parameters.Add(new SqlParameter("@windowSeconds", SqlDbType.Int) { Value = (int)window.TotalSeconds });
            },
            async command => Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);

        return code == 0 ? HostAttestationReclaimOutcome.Reclaimed : HostAttestationReclaimOutcome.Held;
    }

    /// <inheritdoc />
    public async ValueTask<bool> MarkStoppedAsync(string hostId, string processInstanceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentException.ThrowIfNullOrWhiteSpace(processInstanceId);
        var changed = await RunAsync(
            $"UPDATE {Table} SET [StoppedUtc] = SYSUTCDATETIME() WHERE [HostId] = @host AND [ProcessInstanceId] = @process AND [StoppedUtc] IS NULL;",
            command =>
            {
                command.Parameters.Add(new SqlParameter("@host", SqlDbType.NVarChar, 64) { Value = hostId });
                command.Parameters.Add(new SqlParameter("@process", SqlDbType.NVarChar, 64) { Value = processInstanceId });
            },
            async command => await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        return changed > 0;
    }

    /// <inheritdoc />
    public async ValueTask<HostConfigurationAttestationSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
        await RunAsync(
            $"""
            SELECT [HostId], [Source], [SchemaVersion], [ProcessInstanceId], [ProcessStartedUtc], [ClaimedUtc], [RefreshedUtc],
                   [StoppedUtc], [DatabaseFingerprint], [Payload]
              FROM {Table}
             ORDER BY [HostId];
            SELECT SYSUTCDATETIME(), {FingerprintSql};
            """,
            _ => { },
            async command =>
            {
                var rows = new List<HostConfigurationAttestationRow>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(new HostConfigurationAttestationRow(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetInt32(2),
                        reader.GetString(3),
                        reader.GetFieldValue<DateTimeOffset>(4),
                        Utc(reader.GetDateTime(5)),
                        Utc(reader.GetDateTime(6)),
                        reader.IsDBNull(7) ? null : Utc(reader.GetDateTime(7)),
                        reader.GetString(8),
                        reader.GetString(9)));
                }

                // The database's time is read after the rows: a refresh that lands between the two statements can only make a row
                // look older than it is, never younger.
                if (!await reader.NextResultAsync(cancellationToken).ConfigureAwait(false) || !await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException("The database did not report its time.");

                return new HostConfigurationAttestationSnapshot(rows, Utc(reader.GetDateTime(0)), reader.GetString(1));
            },
            cancellationToken).ConfigureAwait(false);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static void Validate(HostConfigurationAttestationWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentException.ThrowIfNullOrWhiteSpace(write.HostId);
        ArgumentException.ThrowIfNullOrWhiteSpace(write.ProcessInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(write.Payload);
        if (write.HostId.Length > 64 || write.ProcessInstanceId.Length > 64)
            throw new ArgumentException("A host id and a process instance id are at most 64 characters.", nameof(write));
    }

    private static void AddWriteParameters(DbCommand command, HostConfigurationAttestationWrite write)
    {
        command.Parameters.Add(new SqlParameter("@host", SqlDbType.NVarChar, 64) { Value = write.HostId });
        command.Parameters.Add(new SqlParameter("@source", SqlDbType.NVarChar, 32) { Value = HostConfigurationAttestationSource.ManagementHost });
        command.Parameters.Add(new SqlParameter("@schemaVersion", SqlDbType.Int) { Value = write.SchemaVersion });
        command.Parameters.Add(new SqlParameter("@process", SqlDbType.NVarChar, 64) { Value = write.ProcessInstanceId });
        command.Parameters.Add(new SqlParameter("@started", SqlDbType.DateTimeOffset) { Value = write.ProcessStartedUtc });
        command.Parameters.Add(new SqlParameter("@payload", SqlDbType.NVarChar, -1) { Value = write.Payload });
    }

    private async ValueTask<T> RunAsync<T>(
        string sql,
        Action<DbCommand> parameters,
        Func<DbCommand, Task<T>> execute,
        CancellationToken cancellationToken)
    {
        var transaction = _db.Database.CurrentTransaction;
        var openedHere = transaction is null;
        if (openedHere)
            await _db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 30;
            if (transaction is not null)
                command.Transaction = transaction.GetDbTransaction();
            parameters(command);
            return await execute(command).ConfigureAwait(false);
        }
        finally
        {
            if (openedHere)
                await _db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
