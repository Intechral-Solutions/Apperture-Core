namespace Apperture.Runtime.Abstractions;

public sealed record WorkflowDefinition(
    WorkflowDefinitionId Id,
    string Name,
    string ModuleId,
    IReadOnlyList<string> StepNames,
    WorkflowCategory Category = WorkflowCategory.Command);

public enum WorkflowCategory
{
    Query,
    Command
}
