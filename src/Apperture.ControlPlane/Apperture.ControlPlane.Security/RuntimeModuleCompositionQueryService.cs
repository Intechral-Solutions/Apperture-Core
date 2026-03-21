using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;

namespace Apperture.ControlPlane.Security;

public sealed class RuntimeModuleCompositionQueryService : IRuntimeModuleCompositionQueryService
{
    private readonly IModuleRegistryService _moduleRegistry;

    public RuntimeModuleCompositionQueryService(IModuleRegistryService moduleRegistry)
    {
        _moduleRegistry = moduleRegistry;
    }

    /// <inheritdoc />
    public async ValueTask<HostEffectiveModuleSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await _moduleRegistry.ListModulesAsync(cancellationToken).ConfigureAwait(false);
        var enabledDescriptors = await _moduleRegistry.GetEnabledDescriptorsAsync(cancellationToken).ConfigureAwait(false);
        var effectivelyExecutableIds = enabledDescriptors.Select(d => d.ModuleId).Distinct(StringComparer.Ordinal).ToList();

        var modules = catalog.Select(ToEntry).ToList();

        return new HostEffectiveModuleSnapshot(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Modules: modules,
            EffectivelyExecutableModuleIds: effectivelyExecutableIds,
            ImmediatelyGovernedExecutionSurfaces: ExecutionSurfaceCatalog.ImmediatelyLifecycleGoverned,
            RestartBoundExecutionSurfaces: ExecutionSurfaceCatalog.RestartBoundSurfaceIds);
    }

    private static RuntimeCompositionModuleEntry ToEntry(ModuleCatalogEntry e) =>
        new(
            ModuleId: e.Descriptor.ModuleId,
            LifecycleState: e.State.ToString(),
            Installed: e.Installed,
            PersistedEnabled: e.Enabled,
            EffectivelyExecutable: e.EffectivelyActive,
            ValidationIssueCodes: e.ValidationIssues.Select(x => x.Code).ToList());
}
