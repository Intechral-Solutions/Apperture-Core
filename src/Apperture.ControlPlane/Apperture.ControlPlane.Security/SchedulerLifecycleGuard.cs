using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Operations;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Lifecycle guard for scheduled workflow and operation dispatch (Epic 27, Epic 29).
/// Uses filtered resolvers so disabled modules return false.
/// </summary>
public sealed class SchedulerLifecycleGuard : ISchedulerLifecycleGuard
{
    private readonly IWorkflowDefinitionResolver _definitionResolver;
    private readonly IOperationRegistryService _operations;

    public SchedulerLifecycleGuard(IWorkflowDefinitionResolver definitionResolver, IOperationRegistryService operations)
    {
        _definitionResolver = definitionResolver;
        _operations = operations;
    }

    /// <inheritdoc />
    public async ValueTask<bool> CanDispatchWorkflowAsync(WorkflowDefinitionId definitionId, CancellationToken cancellationToken = default)
    {
        var definition = await _definitionResolver.GetByIdAsync(definitionId, cancellationToken).ConfigureAwait(false);
        return definition is not null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> CanDispatchOperationAsync(OperationId operationId, CancellationToken cancellationToken = default)
    {
        var definition = await _operations.GetOperationAsync(operationId, cancellationToken).ConfigureAwait(false);
        return definition is not null;
    }
}
