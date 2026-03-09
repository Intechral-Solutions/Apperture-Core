using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;

namespace Sample.HelloWorld;

public sealed class HelloWorldModule : IModule
{
    private static readonly WorkflowDefinitionId DefinitionId = new("Sample.HelloWorld.HelloWorld");
    private const string ModuleId = "Sample.HelloWorld";

    public ModuleManifest GetManifest() => new(ModuleId, "Hello World", "1.0.0", "Sample workflow: Validate → DoWork → Complete");

    public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new WorkflowDefinition(
            DefinitionId,
            "HelloWorld",
            ModuleId,
            new[] { "Validate", "DoWork", "Complete" },
            WorkflowCategory.Command);
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
