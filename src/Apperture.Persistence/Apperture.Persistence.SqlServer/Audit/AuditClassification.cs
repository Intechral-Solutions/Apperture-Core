using System.Data;
using System.Diagnostics;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Apperture.Persistence.SqlServer.Audit;

/// <summary>Tuning for one classification run.</summary>
public sealed record AuditClassificationOptions
{
    public int BatchSize { get; init; } = 5000;

    /// <summary>How long to wait for the classification lock before proceeding with a conditional pass anyway.</summary>
    public TimeSpan LockWait { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Called after each committed batch (progress and tests).</summary>
    public Action<AuditClassificationBatch>? OnBatchCommitted { get; init; }
}

/// <summary>One committed batch: the rows read and the rows this run completed (the rest were completed concurrently).</summary>
public sealed record AuditClassificationBatch(int Number, long FirstId, long LastId, int Read, int Completed);

/// <summary>Counts of one run or preview (<see cref="Applied"/> false), per classification value.</summary>
public sealed record AuditClassificationReport(
    bool Applied,
    bool LockAcquired,
    long Read,
    long Completed,
    int Batches,
    TimeSpan Duration,
    IReadOnlyDictionary<string, long> ByKind,
    IReadOnlyDictionary<string, long> ByAttribution,
    IReadOnlyDictionary<string, long> ByIssue,
    IReadOnlyDictionary<string, long> ByOutcomeProvenance,
    IReadOnlyDictionary<string, long> ByOutcomeCode,
    IReadOnlyDictionary<string, long> ByVisibility)
{
    /// <summary>Rows another classifier completed between this run's read and its update.</summary>
    public long CompletedElsewhere => Applied ? Read - Completed : 0;
}

/// <summary>
/// The re-runnable startup data step that completes unclassified audit rows (AUD-1 design gate §15): rows written before
/// AUD-1.1, rows an earlier binary wrote after a rollback, and <c>append</c> rows whose insert-time classification could
/// not run.
/// </summary>
/// <remarks>
/// <para>Each row gets exactly one transition: one set-based <c>UPDATE</c> per batch sets the whole classification set
/// and <c>ClassificationState = complete</c>, conditional on the row still being unclassified (§11.8 rule 3). A row
/// completed meanwhile by another host is not touched, and the integrity guard independently refuses any change to a
/// completed row. No evidence column is written.</para>
/// <para>Batches go by <c>Id</c>, each in its own transaction, so committed progress survives an interruption and the next
/// start resumes. A named application lock keeps to one classifier at a time; a host that cannot get it in time proceeds
/// with its own conditional pass, because correctness rests on the conditional transition and the guard, not the lock.</para>
/// <para>Classification uses the users, ledger and Entra links as they exist when the step runs. Completed rows are frozen:
/// an automatic rerun never reclassifies one, and cannot turn an unresolved row into a resolved one.</para>
/// </remarks>
public static class AuditClassification
{
    public const string LockResource = "apperture.audit-classification";
    private const int EntraSource = 0;

    /// <summary>True when any row is unclassified: one seek on the filtered unclassified index.</summary>
    public static async Task<bool> AnyUnclassifiedAsync(AppertureDbContext db, CancellationToken cancellationToken = default) =>
        await db.AuditLogs.AsNoTracking().AnyAsync(a => a.ClassificationState == null, cancellationToken).ConfigureAwait(false);

    public static Task<AuditClassificationReport> RunAsync(
        AppertureDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default,
        AuditClassificationOptions? options = null) =>
        RunCoreAsync(db, logger, apply: true, options ?? new AuditClassificationOptions(), cancellationToken);

    /// <summary>Classifies and counts without changing anything.</summary>
    public static Task<AuditClassificationReport> PreviewAsync(
        AppertureDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default,
        AuditClassificationOptions? options = null) =>
        RunCoreAsync(db, logger, apply: false, options ?? new AuditClassificationOptions(), cancellationToken);

    private static async Task<AuditClassificationReport> RunCoreAsync(
        AppertureDbContext db,
        ILogger logger,
        bool apply,
        AuditClassificationOptions options,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var counts = new Counts();
        if (!await AnyUnclassifiedAsync(db, cancellationToken).ConfigureAwait(false))
            return counts.Report(apply, lockAcquired: false, stopwatch.Elapsed);

        var connection = (SqlConnection)db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var lockAcquired = false;
        try
        {
            if (apply)
            {
                lockAcquired = await TryGetLockAsync(connection, options.LockWait, cancellationToken).ConfigureAwait(false);
                if (!lockAcquired)
                {
                    logger.LogInformation(
                        "Audit classification lock is held by another host; continuing with this host's own conditional pass.");
                }

                await ExecuteAsync(connection, null, CreateBatchTableSql, cancellationToken).ConfigureAwait(false);
            }

            var classifier = await LoadClassifierAsync(connection, cancellationToken).ConfigureAwait(false);
            var catalog = AuditActionCatalog.Default;
            var detailActions = catalog.Definitions
                .Where(d => d.LegacyTarget != AuditLegacyTargetSource.None)
                .Select(d => d.Action)
                .ToList();

            long after = 0;
            var batchNumber = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rows = await ReadBatchAsync(connection, after, options.BatchSize, detailActions, cancellationToken).ConfigureAwait(false);
                if (rows.Count == 0)
                    break;

                batchNumber++;
                var classified = rows
                    .Select(r => (r.Id, Set: AuditRowClassifier.Classify(new AuditLegacyRow(r.Actor, r.Action, r.Outcome, r.Details, r.Producer), classifier, catalog)))
                    .ToList();

                var completed = apply
                    ? await ApplyBatchAsync(connection, classified, cancellationToken).ConfigureAwait(false)
                    : rows.Count;
                foreach (var (_, set) in classified)
                    counts.Add(set);
                counts.Read += rows.Count;
                counts.Completed += completed;
                counts.Batches = batchNumber;
                after = rows[^1].Id;

                var batch = new AuditClassificationBatch(batchNumber, rows[0].Id, rows[^1].Id, rows.Count, completed);
                logger.LogInformation(
                    "Audit classification{Mode} batch {Batch}: rows {FirstId}-{LastId}, read {Read}, completed {Completed}.",
                    apply ? "" : " preview",
                    batch.Number,
                    batch.FirstId,
                    batch.LastId,
                    batch.Read,
                    batch.Completed);
                options.OnBatchCommitted?.Invoke(batch);
            }

            var report = counts.Report(apply, lockAcquired, stopwatch.Elapsed);
            logger.LogInformation(
                "Audit classification{Mode} complete: read {Read}, completed {Completed} in {Batches} batch(es), {Duration}. Kinds: {Kinds}. Attribution: {Attribution}. Issues: {Issues}. Outcome provenance: {Provenance}.",
                apply ? "" : " preview",
                report.Read,
                report.Completed,
                report.Batches,
                report.Duration,
                Describe(report.ByKind),
                Describe(report.ByAttribution),
                Describe(report.ByIssue),
                Describe(report.ByOutcomeProvenance));
            return report;
        }
        finally
        {
            if (lockAcquired)
                await ReleaseLockQuietlyAsync(connection).ConfigureAwait(false);
            if (openedHere)
                await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    private static string Describe(IReadOnlyDictionary<string, long> counts) =>
        counts.Count == 0 ? "none" : string.Join(", ", counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Key}={c.Value}"));

    private sealed record Row(long Id, string Actor, string Action, string? Outcome, string? Details, string? Producer);

    private static async Task<List<Row>> ReadBatchAsync(
        SqlConnection connection,
        long after,
        int batchSize,
        IReadOnlyList<string> detailActions,
        CancellationToken cancellationToken)
    {
        // Details are read only for the actions whose typed JSON may supply a target (P-5); nothing else parses them.
        await using var command = connection.CreateCommand();
        var detailParameters = detailActions.Select((_, i) => "@detailAction" + i).ToList();
        var details = detailParameters.Count == 0
            ? "CAST(NULL AS nvarchar(max))"
            : $"CASE WHEN [Action] IN ({string.Join(", ", detailParameters)}) THEN [Details] END";
        command.CommandText = $"""
            SELECT TOP (@batch) [Id], [Actor], [Action], [Outcome], {details}, [Producer]
            FROM [AuditLogs]
            WHERE [ClassificationState] IS NULL AND [Id] > @after
            ORDER BY [Id];
            """;
        command.Parameters.Add(new SqlParameter("@batch", SqlDbType.Int) { Value = batchSize });
        command.Parameters.Add(new SqlParameter("@after", SqlDbType.BigInt) { Value = after });
        for (var i = 0; i < detailActions.Count; i++)
            command.Parameters.Add(new SqlParameter(detailParameters[i], SqlDbType.NVarChar, 128) { Value = detailActions[i] });

        var rows = new List<Row>(batchSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new Row(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return rows;
    }

    private const string CreateBatchTableSql = """
        IF OBJECT_ID(N'tempdb..#AuditClassificationBatch') IS NOT NULL DROP TABLE #AuditClassificationBatch;
        CREATE TABLE #AuditClassificationBatch (
            [Id] bigint NOT NULL PRIMARY KEY,
            [ActorKind] nvarchar(16) NOT NULL,
            [ActorUserId] nvarchar(64) NULL,
            [ActorUsername] nvarchar(256) NULL,
            [ActorAttribution] nvarchar(32) NOT NULL,
            [AttributionIssue] nvarchar(64) NULL,
            [AttributionIssueCertainty] nvarchar(16) NULL,
            [TargetType] nvarchar(64) NULL,
            [TargetId] nvarchar(256) NULL,
            [Category] nvarchar(32) NOT NULL,
            [Severity] nvarchar(16) NOT NULL,
            [Visibility] nvarchar(16) NOT NULL,
            [Basis] nvarchar(32) NOT NULL,
            [OutcomeCode] nvarchar(16) NOT NULL,
            [OutcomeProvenance] nvarchar(16) NOT NULL);
        """;

    /// <summary>
    /// The single transition for one batch, in its own transaction: every column of the set and the state in one
    /// statement, only where the row is still unclassified. Returns the rows this statement completed.
    /// </summary>
    private static async Task<int> ApplyBatchAsync(
        SqlConnection connection,
        IReadOnlyList<(long Id, AuditClassificationSet Set)> rows,
        CancellationToken cancellationToken)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(long));
        foreach (var name in new[]
        {
            "ActorKind", "ActorUserId", "ActorUsername", "ActorAttribution", "AttributionIssue", "AttributionIssueCertainty",
            "TargetType", "TargetId", "Category", "Severity", "Visibility", "Basis", "OutcomeCode", "OutcomeProvenance",
        })
        {
            table.Columns.Add(name, typeof(string));
        }

        foreach (var (id, s) in rows)
        {
            table.Rows.Add(
                id, s.ActorKind, (object?)s.ActorUserId ?? DBNull.Value, (object?)s.ActorUsername ?? DBNull.Value, s.ActorAttribution,
                (object?)s.AttributionIssue ?? DBNull.Value, (object?)s.AttributionIssueCertainty ?? DBNull.Value,
                (object?)s.TargetType ?? DBNull.Value, (object?)s.TargetId ?? DBNull.Value,
                s.Category, s.Severity, s.Visibility, s.Basis, s.OutcomeCode, s.OutcomeProvenance);
        }

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(connection, transaction, "TRUNCATE TABLE #AuditClassificationBatch;", cancellationToken).ConfigureAwait(false);
            using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
            {
                DestinationTableName = "#AuditClassificationBatch",
                BatchSize = 0,
            })
            {
                foreach (DataColumn column in table.Columns)
                    bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                await bulk.WriteToServerAsync(table, cancellationToken).ConfigureAwait(false);
            }

            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            // External columns stay null: no historical or append row is ever classified external (§10.4).
            update.CommandText = """
                UPDATE a
                SET [ClassificationState] = N'complete',
                    [ActorKind] = b.[ActorKind],
                    [ActorUserId] = b.[ActorUserId],
                    [ActorUsername] = b.[ActorUsername],
                    [ActorExternalProvider] = NULL,
                    [ActorExternalTenantId] = NULL,
                    [ActorExternalSubjectId] = NULL,
                    [ActorAttribution] = b.[ActorAttribution],
                    [AttributionIssue] = b.[AttributionIssue],
                    [AttributionIssueCertainty] = b.[AttributionIssueCertainty],
                    [TargetType] = b.[TargetType],
                    [TargetId] = b.[TargetId],
                    [Category] = b.[Category],
                    [Severity] = b.[Severity],
                    [Visibility] = b.[Visibility],
                    [Basis] = b.[Basis],
                    [OutcomeCode] = b.[OutcomeCode],
                    [OutcomeProvenance] = b.[OutcomeProvenance]
                FROM [AuditLogs] AS a
                INNER JOIN #AuditClassificationBatch AS b ON b.[Id] = a.[Id]
                WHERE a.[ClassificationState] IS NULL;
                """;
            var completed = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return completed;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The server rolls back a transaction whose connection broke.
            }

            throw;
        }
    }

    private static async Task<HistoricalActorClassifier> LoadClassifierAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var userIds = await ReadAsync(connection, "SELECT [Id] FROM Users;", r => r.GetString(0), cancellationToken).ConfigureAwait(false);
        var ledger = await ReadAsync(
            connection,
            "SELECT [Username], [UserId] FROM UsernameLedger;",
            r => (r.GetString(0), r.GetString(1)),
            cancellationToken).ConfigureAwait(false);
        var entraLinks = await ReadAsync(
            connection,
            $"SELECT [ExternalId], [UserId] FROM IdentityLinks WHERE [Source] = {EntraSource};",
            r => (r.GetString(0), r.GetString(1)),
            cancellationToken).ConfigureAwait(false);
        return new HistoricalActorClassifier(userIds, ledger, entraLinks);
    }

    private static async Task<bool> TryGetLockAsync(SqlConnection connection, TimeSpan wait, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = @timeout;";
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = LockResource });
        command.Parameters.Add(new SqlParameter("@timeout", SqlDbType.Int) { Value = (int)wait.TotalMilliseconds });
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        command.Parameters.Add(result);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return result.Value is int code && code >= 0;
    }

    private static async Task ReleaseLockQuietlyAsync(SqlConnection connection)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = N'Session';";
            command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = LockResource });
            await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Closing the session releases it.
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<T>> ReadAsync<T>(SqlConnection connection, string sql, Func<SqlDataReader, T> map, CancellationToken cancellationToken)
    {
        var rows = new List<T>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            rows.Add(map(reader));
        return rows;
    }

    private sealed class Counts
    {
        private readonly Dictionary<string, long> _kind = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _attribution = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _issue = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _provenance = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _outcome = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _visibility = new(StringComparer.Ordinal);

        public long Read { get; set; }
        public long Completed { get; set; }
        public int Batches { get; set; }

        public void Add(AuditClassificationSet set)
        {
            Increment(_kind, set.ActorKind);
            Increment(_attribution, set.ActorAttribution);
            Increment(_issue, set.AttributionIssue is null ? "none" : $"{set.AttributionIssue}:{set.AttributionIssueCertainty}");
            Increment(_provenance, set.OutcomeProvenance);
            Increment(_outcome, set.OutcomeCode);
            Increment(_visibility, set.Visibility);
        }

        public AuditClassificationReport Report(bool applied, bool lockAcquired, TimeSpan duration) =>
            new(applied, lockAcquired, Read, Completed, Batches, duration, _kind, _attribution, _issue, _provenance, _outcome, _visibility);

        private static void Increment(Dictionary<string, long> counts, string key) =>
            counts[key] = counts.GetValueOrDefault(key) + 1;
    }
}
