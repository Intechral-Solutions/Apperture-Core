using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class RuntimeModuleCompositionQueryService : IRuntimeModuleCompositionQueryService
{
    private readonly IModuleRegistryService _moduleRegistry;
    private readonly AppertureDbContext? _db;

    public RuntimeModuleCompositionQueryService(
        IModuleRegistryService moduleRegistry,
        AppertureDbContext? db = null)
    {
        _moduleRegistry = moduleRegistry;
        _db = db;
    }

    /// <inheritdoc />
    public async ValueTask<HostEffectiveModuleSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await _moduleRegistry.ListModulesAsync(cancellationToken).ConfigureAwait(false);
        var enabledDescriptors = await _moduleRegistry.GetEnabledDescriptorsAsync(cancellationToken).ConfigureAwait(false);
        var effectivelyExecutableIds = enabledDescriptors.Select(d => d.ModuleId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

        var inFlightByModule = await GetInFlightCountsByModuleAsync(catalog, cancellationToken).ConfigureAwait(false);

        var modules = catalog.Select(e => ToEntry(e, effectivelyExecutableIds, inFlightByModule)).ToList();

        return new HostEffectiveModuleSnapshot(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Modules: modules,
            EffectivelyExecutableModuleIds: effectivelyExecutableIds.ToList(),
            ImmediatelyGovernedExecutionSurfaces: ExecutionSurfaceCatalog.ImmediatelyLifecycleGoverned,
            RestartBoundExecutionSurfaces: ExecutionSurfaceCatalog.RestartBoundSurfaceIds,
            NotYetImplementedExecutionSurfaces: ExecutionSurfaceCatalog.NotYetImplementedSurfaceIds);
    }

    private async Task<IReadOnlyDictionary<string, int>> GetInFlightCountsByModuleAsync(
        IReadOnlyList<ModuleCatalogEntry> catalog,
        CancellationToken cancellationToken)
    {
        if (_db is null || !_db.Database.IsRelational())
            return new Dictionary<string, int>();
        var result = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entry in catalog)
        {
            var workflowIds = entry.Descriptor.Contributions
                .OfType<WorkflowContributionDescriptor>()
                .Select(c => c.Id)
                .ToHashSet(StringComparer.Ordinal);
            var operationIds = entry.Descriptor.Contributions
                .OfType<OperationContributionDescriptor>()
                .Select(c => c.Id)
                .ToHashSet(StringComparer.Ordinal);

            var count = 0;
            if (workflowIds.Count > 0)
            {
                count += await _db.OperationRuns.AsNoTracking()
                    .CountAsync(r =>
                        r.Status == WorkflowLifecycleStatusNames.Running &&
                        workflowIds.Contains(r.DefinitionId),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            if (operationIds.Count > 0)
            {
                count += await _db.OperationInstances.AsNoTracking()
                    .CountAsync(i =>
                        i.Status == WorkflowLifecycleStatusNames.Running &&
                        operationIds.Contains(i.DefinitionId),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (count > 0)
                result[entry.Descriptor.ModuleId] = count;
        }

        return result;
    }

    private static RuntimeCompositionModuleEntry ToEntry(
        ModuleCatalogEntry e,
        IReadOnlySet<string> effectivelyExecutableIds,
        IReadOnlyDictionary<string, int> inFlightByModule)
    {
        var moduleId = e.Descriptor.ModuleId;
        var allowsNewStarts = effectivelyExecutableIds.Contains(moduleId);
        var hasInFlightWork = inFlightByModule.TryGetValue(moduleId, out var count) && count > 0;

        string? executionDegradedReason = null;
        if (!allowsNewStarts)
        {
            executionDegradedReason = hasInFlightWork ? "draining_in_flight" : "module_disabled";
            if (e.Enabled && !e.EffectivelyActive && e.ValidationIssues.Count > 0)
                executionDegradedReason = "invalid_persisted_state";
        }

        return new RuntimeCompositionModuleEntry(
            ModuleId: moduleId,
            LifecycleState: e.State.ToString(),
            Installed: e.Installed,
            PersistedEnabled: e.Enabled,
            EffectivelyExecutable: e.EffectivelyActive,
            ValidationIssueCodes: e.ValidationIssues.Select(x => x.Code).ToList(),
            AllowsNewStarts: allowsNewStarts,
            HasInFlightWork: hasInFlightWork,
            ExecutionDegradedReason: executionDegradedReason);
    }
}
