using Apperture.ModuleSdk.Abstractions;

namespace Apperture.RuntimeHost.Windows.ModuleHost;

public interface IModuleHost
{
    IModule? LoadedModule { get; }
    Task LoadAsync(IModule module, CancellationToken cancellationToken = default);
}
