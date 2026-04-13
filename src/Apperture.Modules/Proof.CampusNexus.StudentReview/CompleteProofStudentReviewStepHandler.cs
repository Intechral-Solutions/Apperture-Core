using System.Data;
using Apperture.Core.Application.Execution;

namespace Proof.CampusNexus.StudentReview;

public sealed class CompleteProofStudentReviewStepHandler : IStepHandler
{
    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var db = await context.ResolveSlotAsync<IDbConnection>(ProofCampusNexusStudentReviewConstants.SlotIntegrationDb, cancellationToken)
            .ConfigureAwait(false);
        if (db is null)
            return OperatorExecutionResult.SuccessSummaryOnly(new { message = "Proof StudentReview workflow completed (Db slot not bound)." });

        using (db)
        {
            db.Open();
            return OperatorExecutionResult.SuccessSummaryOnly(new
            {
                message = "Proof CampusNexus StudentReview workflow completed.",
                step = "Complete"
            });
        }
    }
}
