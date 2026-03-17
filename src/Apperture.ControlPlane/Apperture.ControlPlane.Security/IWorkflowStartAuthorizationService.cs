using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Authorizes workflow start. Uses module-declared workflow.execute when the workflow is owned by a module,
/// otherwise platform workflow.start at instance scope.
/// </summary>
public interface IWorkflowStartAuthorizationService
{
    /// <summary>
    /// Ensures the current actor is allowed to start the given workflow. Throws <see cref="UnauthorizedAccessException"/> if not.
    /// </summary>
    ValueTask EnsureCanStartWorkflowAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default);
}
