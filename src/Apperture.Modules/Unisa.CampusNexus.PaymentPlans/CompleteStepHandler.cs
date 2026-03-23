using Apperture.Core.Application.Execution;

namespace Unisa.CampusNexus.PaymentPlans;

public sealed class CompleteStepHandler : IStepHandler
{
    public ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<object?>(OperatorExecutionResult.SuccessInline(new { step = "Complete", message = "UNISA Campus Nexus payment plans workflow finished." }));
}
