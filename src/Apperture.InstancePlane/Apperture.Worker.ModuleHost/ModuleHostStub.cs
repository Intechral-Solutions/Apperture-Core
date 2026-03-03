using Apperture.ModuleSdk.Abstractions;

namespace Apperture.Worker.ModuleHost;

public sealed class ModuleHostStub : IModuleHost
{
    public IModule? LoadedModule { get; private set; }

    public Task LoadAsync(IModule module, CancellationToken cancellationToken = default)
    {
        LoadedModule = module;
        return Task.CompletedTask;
    }
}
