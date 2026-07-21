using System.Data;
using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.CampusNexus.Models;
using Apperture.Core.Application.Execution;
using Apperture.ModuleSdk.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Proof.CampusNexus.StudentReview;

/// <summary>
/// Queries CNS OData for recently modified students and upserts module-owned review rows into the integration-bound SQL database.
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

        var sqlConn = await context.ResolveSlotAsync<IDbConnection>(
                ProofCampusNexusStudentReviewConstants.SlotIntegrationDb,
                cancellationToken)
            .ConfigureAwait(false);
        if (sqlConn is null)
        {
            throw OperatorExecutionResult.FailInline(
                $"Slot '{ProofCampusNexusStudentReviewConstants.SlotIntegrationDb}' is not bound to an active SQL connection for this integration.",
                new { code = "proof.studentReview.missingDbSlot" });
        }

        string connectionString;
        using (sqlConn)
        {
            // Read the raw connection string before any Open() call.
            // SqlConnection may redact sensitive fields (for example password) once opened unless Persist Security Info is enabled.
            connectionString = sqlConn.ConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw OperatorExecutionResult.FailInline(
                    "Integration SQL connection has no connection string.",
                    new { code = "proof.studentReview.missingConnectionString" });
            }
        }

        using var scope = _scopeFactory.CreateScope();
        var persistence = scope.ServiceProvider.GetRequiredService<IProofStudentReviewPersistenceAdapter>();
        var persistedRun = await persistence.StartRunAsync(
                connectionString,
                integrationId,
                runId,
                entitySet,
                odataQuery,
                cancellationToken)
            .ConfigureAwait(false);

        var cns = await context.ResolveSlotAsync<ICampusNexusApiClient>(ProofCampusNexusStudentReviewConstants.SlotCnsApi, cancellationToken)
            .ConfigureAwait(false);
        if (cns is null)
        {
            await persistence.MarkRunFailedAsync(persistedRun, "Campus Nexus slot not bound.", cancellationToken).ConfigureAwait(false);
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
                await persistence.MarkRunFailedAsync(persistedRun, msg, cancellationToken).ConfigureAwait(false);
                throw OperatorExecutionResult.FailInline(
                    $"Campus Nexus query failed: {ex.Message}",
                    new { code = "proof.studentReview.cnsError", entitySet, odataQuery });
            }

            var persistenceResult = await persistence.CompleteRunAsync(
                    persistedRun,
                    rows.Select(row => new ProofStudentReviewSnapshotInput(
                            row.Id,
                            row.StudentNumber,
                            row.FirstName,
                            row.LastName,
                            row.CreatedDateTime,
                            row.LastModifiedDateTime))
                        .ToList(),
                    parameters.ReadString("correlationId"),
                    cancellationToken)
                .ConfigureAwait(false);

            context.Signals.Logs.LogOperational(
                OperationalLogLevel.Information,
                "proof.studentReview.sync.completed",
                "Student review sync completed.",
                new Dictionary<string, object?>
                {
                    ["integrationId"] = integrationId,
                    ["entitySet"] = entitySet,
                    ["retrieved"] = persistenceResult.RetrievedCount,
                    ["inserted"] = persistenceResult.InsertedCount,
                    ["updated"] = persistenceResult.UpdatedCount
                });

            return OperatorExecutionResult.SuccessInline(new
            {
                step = "SyncRecentStudents",
                entitySet,
                odataQuery,
                retrieved = persistenceResult.RetrievedCount,
                inserted = persistenceResult.InsertedCount,
                updated = persistenceResult.UpdatedCount,
                maxLastModifiedDateTime = persistenceResult.MaxLastModifiedDateTimeSeen,
                integrationId,
                runId
            });
        }
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..max];
    }
}
