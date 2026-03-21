using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Resolves the owning module id for a workflow definition.
/// Used to apply module-scoped permissions (e.g. workflow.execute at module:{moduleId}) when starting or listing workflows.
/// Implementations should consider only modules that are effectively enabled for execution (Epic 27).
/// </summary>
public interface IWorkflowModuleResolver
{
    /// <summary>
    /// Returns the module id that owns the given workflow definition among effectively enabled modules, or null if none.
    /// </summary>
    ValueTask<string?> GetModuleIdForWorkflowAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default);
}
