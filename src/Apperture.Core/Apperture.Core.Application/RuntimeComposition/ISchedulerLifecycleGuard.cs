using Apperture.Core.Domain.Operations;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.Core.Application.RuntimeComposition;

/// <summary>
/// Lifecycle guard for scheduled workflow and operation dispatch (Epic 27, Epic 29).
/// Before dispatching scheduled work, the scheduler must call this; if it returns false,
/// the work must be skipped and recorded as blocked (audit/log).
/// </summary>
public interface ISchedulerLifecycleGuard
{
    /// <summary>
    /// Returns true if the workflow definition is executable (module enabled, etc.).
    /// Returns false when the module is disabled or the definition is unavailable.
    /// </summary>
    ValueTask<bool> CanDispatchWorkflowAsync(WorkflowDefinitionId definitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if the operation definition is executable (module enabled, etc.).
    /// Returns false when the module is disabled or the definition is unavailable.
    /// </summary>
    ValueTask<bool> CanDispatchOperationAsync(OperationId operationId, CancellationToken cancellationToken = default);
}
