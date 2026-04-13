using System.Data;
using Apperture.Connectors.CampusNexus;
using Apperture.Core.Application.Execution;

namespace Proof.CampusNexus.StudentReview;

/// <summary>
/// Verifies integration bindings for CNS API and platform SQL before sync.
/// </summary>
public sealed class ValidateProofStudentReviewStepHandler : IStepHandler
{
    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.IntegrationId))
        {
            throw OperatorExecutionResult.FailInline(
                "IntegrationId is required. Start the workflow with integrationId in the API body.",
                new { code = "proof.studentReview.missingIntegration" });
        }

        var cns = await context.ResolveSlotAsync<ICampusNexusApiClient>(ProofCampusNexusStudentReviewConstants.SlotCnsApi, cancellationToken)
            .ConfigureAwait(false);
        if (cns is null)
        {
            throw OperatorExecutionResult.FailInline(
                $"Slot '{ProofCampusNexusStudentReviewConstants.SlotCnsApi}' is not bound to an active Campus Nexus connection for this integration.",
                new { code = "proof.studentReview.missingCnsSlot" });
        }

        using (cns)
        {
            var db = await context.ResolveSlotAsync<IDbConnection>(ProofCampusNexusStudentReviewConstants.SlotIntegrationDb, cancellationToken)
                .ConfigureAwait(false);
            if (db is null)
            {
                throw OperatorExecutionResult.FailInline(
                    $"Slot '{ProofCampusNexusStudentReviewConstants.SlotIntegrationDb}' is not bound to an active SQL connection for this integration.",
                    new { code = "proof.studentReview.missingDbSlot" });
            }

            using (db)
            {
                db.Open();
                return OperatorExecutionResult.SuccessInline(new
                {
                    step = "Validate",
                    message = "Campus Nexus and SQL slots resolve for this integration.",
                    integrationId = context.IntegrationId
                });
            }
        }
    }
}
