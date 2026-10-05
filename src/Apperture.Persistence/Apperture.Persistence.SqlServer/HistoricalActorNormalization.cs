using System.Data;
using Apperture.Core.Application.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Apperture.Persistence.SqlServer;

/// <summary>A non-audit column that stores who did something, normalized by <see cref="HistoricalActorNormalization"/>.</summary>
public sealed record HistoricalActorColumn(string Table, string Column)
{
    public override string ToString() => $"{Table}.{Column}";
}

/// <summary>
/// Row counts for one column. Every non-null row is counted in exactly one category, so the categories add up to
/// <see cref="NonNullRows"/>. <see cref="Resolved"/> counts rows actually rewritten (by a preview: rows that would be).
/// </summary>
public sealed record HistoricalActorColumnResult(
    HistoricalActorColumn Column,
    long Resolved,
    long AlreadyUserId,
    long Literal,
    long Unresolved,
    long Ambiguous,
    long Null,
    int DistinctValues)
{
    public long NonNullRows => Resolved + AlreadyUserId + Literal + Unresolved + Ambiguous;
}

/// <summary>The result of one normalization run, or of a preview (<see cref="Applied"/> false).</summary>
public sealed record HistoricalActorNormalizationReport(bool Applied, IReadOnlyList<HistoricalActorColumnResult> Columns)
{
    public long Resolved => Columns.Sum(c => c.Resolved);
    public long AlreadyUserId => Columns.Sum(c => c.AlreadyUserId);
    public long Literal => Columns.Sum(c => c.Literal);
    public long Unresolved => Columns.Sum(c => c.Unresolved);
    public long Ambiguous => Columns.Sum(c => c.Ambiguous);
}

/// <summary>
/// Rewrites legacy human actor values (usernames, retired usernames and Entra object ids written before sessions were
/// keyed by user id) in the approved non-audit columns to user ids, where and only where that is safe
/// (<see cref="HistoricalActorClassifier"/>). Literals, unresolved and ambiguous values stay verbatim. Nothing is created:
/// no user, identity link or ledger row. <c>AuditLogs.Actor</c> is never touched.
/// </summary>
/// <remarks>
/// <para>Re-runnable by design, and run by the Management Host on every start: an earlier binary (after a rollback) writes
/// usernames and object ids again, and the next start of this version normalizes them. On a normalized database a run
/// only reads. There is no completion flag.</para>
/// <para>Per column: one grouped scan returns each distinct value (byte-exact: binary collation plus length, so a case or
/// trailing-space variant is its own value) with its row count; each value is classified once against identity data
/// loaded once per run (users, the username ledger, Entra links); each resolvable value gets one conditional update that
/// matches only rows still holding that exact original value. A value changed by another writer in between is not
/// overwritten. Each update commits on its own, so an interrupted run converges on the next one.</para>
/// </remarks>
public static class HistoricalActorNormalization
{
    private const int EntraSource = 0;

    /// <summary>The approved scope (IAM-9 design gate §17). Never <c>AuditLogs.Actor</c>.</summary>
    public static IReadOnlyList<HistoricalActorColumn> Columns { get; } =
    [
        new("RuntimeControlRequests", "RequestedBy"),
        new("WorkflowExternalControlRequests", "RequestedBy"),
        new("PlatformSettingValues", "UpdatedByPrincipalId"),
        new("EntraAccessRequests", "ReviewedByUserId"),
        new("AttentionIncidents", "AcknowledgedBy"),
        new("OperationItemAttentionAcknowledgements", "AcknowledgedByUserId"),
        new("OperationItemAttentionAcknowledgements", "ReopenedByUserId"),
        new("OperationItemRetryAttempts", "RequestedByActorId"),
        new("NotificationDefinitions", "CreatedByActorId"),
        new("NotificationDefinitions", "UpdatedByActorId"),
        new("NotificationSubscriptions", "CreatedByActorId"),
        new("NotificationSubscriptions", "UpdatedByActorId"),
        new("NotificationSubscriptionUserOverrides", "CreatedByActorId"),
    ];

    /// <summary>Normalizes every approved column and reports per-column row counts.</summary>
    /// <param name="onColumnCompleted">Called after each column is done (progress reporting and tests).</param>
    public static Task<HistoricalActorNormalizationReport> RunAsync(
        AppertureDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default,
        Action<HistoricalActorColumnResult>? onColumnCompleted = null) =>
        RunCoreAsync(db, logger, apply: true, onColumnCompleted, cancellationToken);

    /// <summary>Classifies and counts without changing anything (<see cref="HistoricalActorColumnResult.Resolved"/> = rows that would be rewritten).</summary>
    public static Task<HistoricalActorNormalizationReport> PreviewAsync(
        AppertureDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(db, logger, apply: false, onColumnCompleted: null, cancellationToken);

    private static async Task<HistoricalActorNormalizationReport> RunCoreAsync(
        AppertureDbContext db,
        ILogger logger,
        bool apply,
        Action<HistoricalActorColumnResult>? onColumnCompleted,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var classifier = await LoadClassifierAsync(connection, cancellationToken).ConfigureAwait(false);
            var results = new List<HistoricalActorColumnResult>(Columns.Count);
            foreach (var column in Columns)
            {
                var result = await NormalizeColumnAsync(connection, classifier, column, apply, cancellationToken).ConfigureAwait(false);
                results.Add(result);
                logger.LogInformation(
                    "Historical actor normalization{Mode} {Table}.{Column} (rows): Resolved={Resolved}, AlreadyUserId={AlreadyUserId}, Literal={Literal}, Unresolved={Unresolved}, Ambiguous={Ambiguous}, Null={Null}; DistinctValues={DistinctValues}",
                    apply ? "" : " preview",
                    column.Table,
                    column.Column,
                    result.Resolved,
                    result.AlreadyUserId,
                    result.Literal,
                    result.Unresolved,
                    result.Ambiguous,
                    result.Null,
                    result.DistinctValues);
                onColumnCompleted?.Invoke(result);
            }

            var report = new HistoricalActorNormalizationReport(apply, results);
            logger.LogInformation(
                "Historical actor normalization{Mode} complete across {ColumnCount} columns (rows): Resolved={Resolved}, AlreadyUserId={AlreadyUserId}, Literal={Literal}, Unresolved={Unresolved}, Ambiguous={Ambiguous}",
                apply ? "" : " preview",
                results.Count,
                report.Resolved,
                report.AlreadyUserId,
                report.Literal,
                report.Unresolved,
                report.Ambiguous);
            return report;
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync().ConfigureAwait(false);
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

    private static async Task<HistoricalActorColumnResult> NormalizeColumnAsync(
        SqlConnection connection,
        HistoricalActorClassifier classifier,
        HistoricalActorColumn column,
        bool apply,
        CancellationToken cancellationToken)
    {
        // Identifiers come only from the fixed Columns list above, never from input.
        var target = $"[{column.Table}]";
        var actor = $"[{column.Column}]";

        var groups = await ReadAsync(
            connection,
            $"""
            SELECT {actor} COLLATE Latin1_General_BIN2, COUNT_BIG(*)
            FROM {target}
            GROUP BY {actor} COLLATE Latin1_General_BIN2, DATALENGTH({actor});
            """,
            r => (Value: r.IsDBNull(0) ? null : r.GetString(0), Rows: r.GetInt64(1)),
            cancellationToken).ConfigureAwait(false);

        long resolved = 0, alreadyUserId = 0, literal = 0, unresolved = 0, ambiguous = 0, nulls = 0;
        var distinct = 0;
        foreach (var (value, rows) in groups)
        {
            if (value is null)
            {
                nulls += rows;
                continue;
            }

            distinct++;
            var classification = classifier.Classify(value);
            switch (classification.Category)
            {
                case HistoricalActorCategory.Resolved:
                    resolved += apply
                        ? await RewriteAsync(connection, target, actor, value, classification.UserId!, cancellationToken).ConfigureAwait(false)
                        : rows;
                    break;
                case HistoricalActorCategory.AlreadyUserId:
                    alreadyUserId += rows;
                    break;
                case HistoricalActorCategory.Literal:
                    literal += rows;
                    break;
                case HistoricalActorCategory.Ambiguous:
                    ambiguous += rows;
                    break;
                default:
                    unresolved += rows;
                    break;
            }
        }

        return new HistoricalActorColumnResult(column, resolved, alreadyUserId, literal, unresolved, ambiguous, nulls, distinct);
    }

    /// <summary>Rewrites only rows that still hold exactly <paramref name="original"/>; returns how many were rewritten.</summary>
    private static async Task<long> RewriteAsync(
        SqlConnection connection,
        string target,
        string actor,
        string original,
        string userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {target}
            SET {actor} = @userId
            WHERE {actor} COLLATE Latin1_General_BIN2 = @original AND DATALENGTH({actor}) = DATALENGTH(@original);
            """;
        command.Parameters.Add(new SqlParameter("@userId", SqlDbType.NVarChar, -1) { Value = userId });
        command.Parameters.Add(new SqlParameter("@original", SqlDbType.NVarChar, -1) { Value = original });
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<T>> ReadAsync<T>(
        SqlConnection connection,
        string sql,
        Func<SqlDataReader, T> map,
        CancellationToken cancellationToken)
    {
        var rows = new List<T>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            rows.Add(map(reader));
        return rows;
    }
}
