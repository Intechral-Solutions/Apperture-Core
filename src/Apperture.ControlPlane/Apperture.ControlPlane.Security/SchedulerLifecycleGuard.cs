using Apperture.Core.Application.RuntimeComposition;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Lifecycle guard for scheduled workflow dispatch (Epic 27 corrective pass).
/// Uses filtered definition resolver so disabled modules return false.
/// </summary>
public sealed class SchedulerLifecycleGuard : ISchedulerLifecycleGuard
{
    private readonly IWorkflowDefinitionResolver _definitionResolver;

    public SchedulerLifecycleGuard(IWorkflowDefinitionResolver definitionResolver)
    {
        _definitionResolver = definitionResolver;
    }

    /// <inheritdoc />
    public async ValueTask<bool> CanDispatchWorkflowAsync(WorkflowDefinitionId definitionId, CancellationToken cancellationToken = default)
    {
        var definition = await _definitionResolver.GetByIdAsync(definitionId, cancellationToken).ConfigureAwait(false);
        return definition is not null;
    }
}
