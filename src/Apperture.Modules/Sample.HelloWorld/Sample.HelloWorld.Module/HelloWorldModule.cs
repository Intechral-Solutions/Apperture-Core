using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Apperture.Core.Application.Execution;

namespace Sample.HelloWorld;

public sealed class HelloWorldModule : IModule, IModuleStepHandlerRegistrar
{
    private static readonly WorkflowDefinitionId DefinitionId = new("Sample.HelloWorld.HelloWorld");
    private const string ModuleId = "Sample.HelloWorld";

    public ModuleManifest GetManifest() => new(ModuleId, "Hello World", "1.0.0", "Sample workflow: Validate → DoWork → Complete")
    {
        Permissions =
        [
            new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance")
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

    public void RegisterStepHandlers(IStepHandlerRegistry registry)
    {
        // Register handlers for every step declared by the workflow definition so the sample
        // truthfully exercises the worker-side step pipeline and step-instance recording.
        registry.Register(ModuleId, "Validate", new HelloWorldValidateStepHandler());
        registry.Register(ModuleId, "DoWork", new HelloWorldDoWorkStepHandler());
        registry.Register(ModuleId, "Complete", new HelloWorldCompleteStepHandler());
    }
}
