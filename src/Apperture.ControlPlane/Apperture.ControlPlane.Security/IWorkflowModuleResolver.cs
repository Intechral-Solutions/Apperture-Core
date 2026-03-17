using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Resolves the owning module id for a workflow definition.
/// Used to apply module-scoped permissions (e.g. workflow.execute at module:{moduleId}) when starting or listing workflows.
/// </summary>
public interface IWorkflowModuleResolver
{
    /// <summary>
    /// Returns the module id that owns the given workflow definition, or null if no known module owns it.
    /// </summary>
    ValueTask<string?> GetModuleIdForWorkflowAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default);
}
