namespace Apperture.Core.Application.RuntimeComposition;

/// <summary>
/// Named execution surfaces for operator-visible composition projections (Epic 27).
/// </summary>
public static class ExecutionSurfaceCatalog
{
    public static IReadOnlyList<string> ImmediatelyLifecycleGoverned { get; } =
    [
        "WorkflowDefinitionResolver",
        "OperationRegistryService",
        "WorkflowStartDispatchService",
        "OperationStartDispatchService",
        "WorkflowStartRequestProcessor.definition_resolution",
        "OperationStartRequestProcessor.definition_resolution",
        "ModuleWorkflowListingService",
        "MenusController.navigation"
    ];

    public static IReadOnlyList<string> RestartBoundSurfaceIds { get; } =
        Enum.GetNames<RestartBoundSurface>();
}
