using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ModuleSdk;

public abstract class ModuleBase : IModule
{
    public abstract ModuleManifest GetManifest();

    public abstract IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync(CancellationToken cancellationToken = default);
}
