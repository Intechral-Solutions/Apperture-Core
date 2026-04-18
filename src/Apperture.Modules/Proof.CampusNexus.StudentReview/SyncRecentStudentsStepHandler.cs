using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.CampusNexus.Models;
using Apperture.Core.Application.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proof.CampusNexus.StudentReview.Persistence;

namespace Proof.CampusNexus.StudentReview;

/// <summary>
/// Queries CNS OData for recently modified students and upserts module-owned review rows.
/// </summary>
public sealed class SyncRecentStudentsStepHandler : IStepHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SyncRecentStudentsStepHandler> _logger;

    public SyncRecentStudentsStepHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<SyncRecentStudentsStepHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var integrationId = context.IntegrationId?.Trim();
        if (string.IsNullOrEmpty(integrationId))
        {
            throw OperatorExecutionResult.FailInline(
                "IntegrationId is required on the workflow start request.",
                new { code = "proof.studentReview.missingIntegration" });
        }

        var runId = parameters.ReadString(ProofStudentReviewExecutionKeys.RunId);
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw OperatorExecutionResult.FailInline(
                "Internal run id missing from step parameters.",
                new { code = "proof.studentReview.missingRunId" });
        }

        var entitySet = parameters.ReadString("entitySet")?.Trim();
        if (string.IsNullOrEmpty(entitySet))
            entitySet = ProofStudentReviewODataQuery.DefaultEntitySet;

        var orderBy = parameters.ReadString("odataOrderBy")?.Trim();
        if (string.IsNullOrEmpty(orderBy))
            orderBy = ProofStudentReviewODataQuery.DefaultOrderBy;

        var top = parameters.ReadInt("odataTop", ProofStudentReviewODataQuery.DefaultTop);
        if (top < 1)
            top = ProofStudentReviewODataQuery.DefaultTop;
        if (top > 100)
            top = 100;

        var select = parameters.ReadString("odataSelect")?.Trim();
        if (string.IsNullOrEmpty(select))
            select = ProofStudentReviewODataQuery.DefaultSelect;

        var odataQuery = ProofStudentReviewODataQuery.Build(orderBy, top, select);
        var apiQuery = new ApiQuery(entitySet, odataQuery);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProofStudentReviewDbContext>();

        var runEntity = new ProofStudentReviewRunEntity
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            IntegrationId = integrationId,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Status = "running",
            EntitySetUsed = entitySet,
            ODataQueryUsed = odataQuery
        };
        db.StudentReviewRuns.Add(runEntity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var cns = await context.ResolveSlotAsync<ICampusNexusApiClient>(ProofCampusNexusStudentReviewConstants.SlotCnsApi, cancellationToken)
            .ConfigureAwait(false);
        if (cns is null)
        {
            await MarkRunFailedAsync(db, runEntity, "Campus Nexus slot not bound.", cancellationToken).ConfigureAwait(false);
            throw OperatorExecutionResult.FailInline(
                "Campus Nexus API client could not be resolved.",
                new { code = "proof.studentReview.missingCnsSlot" });
        }

        using (cns)
        {
            IReadOnlyList<CnsStudentReviewODataRow> rows;
            try
            {
                _logger.LogInformation(
                    "Proof StudentReview: querying CNS entitySet={EntitySet} odata={OData}",
                    entitySet,
                    odataQuery);
                rows = await cns.QueryAsync<CnsStudentReviewODataRow>(apiQuery, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var msg = Truncate(ex.Message, 3800);
                await MarkRunFailedAsync(db, runEntity, msg, cancellationToken).ConfigureAwait(false);
                throw OperatorExecutionResult.FailInline(
                    $"Campus Nexus query failed: {ex.Message}",
                    new { code = "proof.studentReview.cnsError", entitySet, odataQuery });
            }

            var now = DateTimeOffset.UtcNow;
            var inserted = 0;
            var updated = 0;
            DateTimeOffset? maxMod = null;

            foreach (var row in rows)
            {
                if (row.LastModifiedDateTime is { } dlm && (maxMod is null || dlm > maxMod))
                    maxMod = dlm;

                var existing = await db.StudentReviewSnapshots
                    .FirstOrDefaultAsync(
                        x => x.IntegrationId == integrationId && x.CnsStudentId == row.Id,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    db.StudentReviewSnapshots.Add(new ProofStudentReviewSnapshotEntity
                    {
                        Id = Guid.NewGuid(),
                        IntegrationId = integrationId,
                        CnsStudentId = row.Id,
                        StudentNumber = row.StudentNumber,
                        FirstName = row.FirstName,
                        LastName = row.LastName,
                        CreatedDateTime = row.CreatedDateTime,
                        LastModifiedDateTime = row.LastModifiedDateTime,
                        LastFetchedAtUtc = now,
                        LastUpdatedAtUtc = now,
                        SourceRunId = runId,
                        LastCorrelationId = parameters.ReadString("correlationId")
                    });
                    inserted++;
                }
                else
                {
                    existing.StudentNumber = row.StudentNumber;
                    existing.FirstName = row.FirstName;
                    existing.LastName = row.LastName;
                    existing.CreatedDateTime = row.CreatedDateTime;
                    existing.LastModifiedDateTime = row.LastModifiedDateTime;
                    existing.LastFetchedAtUtc = now;
                    existing.LastUpdatedAtUtc = now;
                    existing.SourceRunId = runId;
                    existing.LastCorrelationId = parameters.ReadString("correlationId");
                    updated++;
                }
            }

            runEntity.CompletedAtUtc = DateTimeOffset.UtcNow;
            runEntity.RetrievedCount = rows.Count;
            runEntity.InsertedCount = inserted;
            runEntity.UpdatedCount = updated;
            runEntity.MaxLastModifiedDateTimeSeen = maxMod;
            runEntity.Status = "succeeded";
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return OperatorExecutionResult.SuccessInline(new
            {
                step = "SyncRecentStudents",
                entitySet,
                odataQuery,
                retrieved = rows.Count,
                inserted,
                updated,
                maxLastModifiedDateTime = maxMod,
                integrationId,
                runId
            });
        }
    }

    private static async Task MarkRunFailedAsync(
        ProofStudentReviewDbContext db,
        ProofStudentReviewRunEntity runEntity,
        string error,
        CancellationToken cancellationToken)
    {
        runEntity.CompletedAtUtc = DateTimeOffset.UtcNow;
        runEntity.Status = "failed";
        runEntity.ErrorSummary = Truncate(error, 3800);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // best-effort
        }
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..max];
    }
}
