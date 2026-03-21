using Apperture.RuntimePlane.Abstractions;

namespace Apperture.Core.Application.RuntimeComposition;

/// <summary>
/// Lifecycle guard for scheduled workflow dispatch (Epic 27 corrective pass).
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
}
