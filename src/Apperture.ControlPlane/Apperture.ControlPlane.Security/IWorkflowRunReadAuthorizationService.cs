using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

public interface IWorkflowRunReadAuthorizationService
{
    /// <summary>
    /// Ensures the current actor is allowed to read runs for the given workflow definition.
    /// Throws <see cref="UnauthorizedAccessException" /> when not authorized.
    /// </summary>
    ValueTask EnsureCanReadRunsAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default);
}

