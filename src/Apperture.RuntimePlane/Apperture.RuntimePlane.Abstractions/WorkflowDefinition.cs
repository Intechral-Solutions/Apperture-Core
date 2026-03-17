using Apperture.Shared.Kernel;

namespace Apperture.RuntimePlane.Abstractions;

public sealed record WorkflowDefinition(
    WorkflowDefinitionId Id,
    string Name,
    string ModuleId,
    IReadOnlyList<string> StepNames,
    WorkflowCategory Category = WorkflowCategory.Command,
    IReadOnlyList<PermissionDescriptor>? RequiredPermissions = null);

public enum WorkflowCategory
{
    Query,
    Command
}
