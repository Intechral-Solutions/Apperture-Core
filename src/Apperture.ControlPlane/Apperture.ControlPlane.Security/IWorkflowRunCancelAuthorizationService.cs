using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

public interface IWorkflowRunCancelAuthorizationService
{
    ValueTask EnsureCanCancelRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default);
}

