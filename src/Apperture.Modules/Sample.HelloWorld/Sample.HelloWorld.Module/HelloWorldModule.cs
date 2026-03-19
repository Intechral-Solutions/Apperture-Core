using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Operations;
using Apperture.Shared.Kernel;
using Apperture.Core.Application.Execution;

namespace Sample.HelloWorld;

public sealed class HelloWorldModule : IModule, IModuleStepHandlerRegistrar, IOperationDefinitionProvider
{
    private static readonly WorkflowDefinitionId DefinitionId = new("Sample.HelloWorld.HelloWorld");
    private static readonly OperationId OperationDefId = new("Sample.HelloWorld.DoWork");
    private const string ModuleId = "Sample.HelloWorld";

    public ModuleManifest GetManifest() => new(ModuleId, "Hello World", "1.0.0", "Sample workflow: Validate → DoWork → Complete")
    {
        Permissions =
        [
            new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance")
            ,
            // Allow operation execution for this module's operations.
            new ModulePermissionDeclaration(ActionConstants.OperationExecute, ResourceTypeConstants.OperationDefinition)
        ]
    };

    public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new WorkflowDefinition(
            DefinitionId,
            "HelloWorld",
            ModuleId,
            new[] { "Validate", "DoWork", "Complete" },
            WorkflowCategory.Command,
            RequiredPermissions:
            [
                new PermissionDescriptor("workflow.execute", new ResourceDescriptor("WorkflowInstance", $"module:{ModuleId}"))
            ]);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async IAsyncEnumerable<OperationDefinition> GetOperationsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new OperationDefinition(
            OperationDefId,
            // MVP contract: `OperationDefinition.Name` is used as the single handler "step name".
            "DoWork",
            ModuleId,
            OperationCategory.Command,
            ParameterSchema: null,
            RequiredPermissions:
            [
                new PermissionDescriptor(
                    ActionConstants.OperationExecute,
                    new ResourceDescriptor(ResourceTypeConstants.OperationDefinition, ScopeKeyConstants.Module(ModuleId)))
            ]);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public void RegisterStepHandlers(IStepHandlerRegistry registry)
    {
        // Register handlers for every step declared by the workflow definition so the sample
        // truthfully exercises the worker-side step pipeline and step-instance recording.
        registry.Register(ModuleId, "Validate", new HelloWorldValidateStepHandler());
        registry.Register(ModuleId, "DoWork", new HelloWorldDoWorkStepHandler());
        registry.Register(ModuleId, "Complete", new HelloWorldCompleteStepHandler());
    }
}
