namespace Apperture.ModuleSdk.Abstractions;

public interface IModule : IModuleManifest, IWorkflowDefinitionProvider
{
    /// <summary>
    /// Returns the explicit descriptor used by platform module lifecycle and contribution catalogs.
    /// </summary>
    ValueTask<ModuleDescriptor> BuildDescriptorAsync(CancellationToken cancellationToken = default)
        => ModuleDescriptorBuilder.BuildAsync(this, cancellationToken);
}
