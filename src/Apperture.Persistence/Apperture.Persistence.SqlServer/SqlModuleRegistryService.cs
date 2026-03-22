using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlModuleRegistryService : IModuleRegistryService
{
    private readonly IEnumerable<IModule> _modules;
    private readonly AppertureDbContext _dbContext;
    private readonly IEnvironmentContext _environmentContext;
    private readonly IAuditService _auditService;
    private readonly IPrincipalContext? _principalContext;

    public SqlModuleRegistryService(
        IEnumerable<IModule> modules,
        AppertureDbContext dbContext,
        IEnvironmentContext environmentContext,
        IAuditService auditService,
        IPrincipalContext? principalContext = null)
    {
        _modules = modules ?? [];
        _dbContext = dbContext;
        _environmentContext = environmentContext;
        _auditService = auditService;
        _principalContext = principalContext;
    }

    public async ValueTask<IReadOnlyList<ModuleCatalogEntry>> ListModulesAsync(CancellationToken cancellationToken = default)
    {
        var discovered = await DiscoverDescriptorsAsync(cancellationToken).ConfigureAwait(false);
        var installs = await GetInstallationsByModuleIdAsync(cancellationToken).ConfigureAwait(false);
        var issues = BuildCrossModuleIssues(discovered, installs, onlyEnabled: true);
        var discoveredById = discovered.ToDictionary(x => x.ModuleId, StringComparer.Ordinal);
        var list = new List<ModuleCatalogEntry>(Math.Max(discovered.Count, installs.Count));

        foreach (var descriptor in discovered)
        {
            installs.TryGetValue(descriptor.ModuleId, out var install);
            list.Add(ToCatalogEntry(descriptor, install, issues));
        }

        foreach (var install in installs.Values.Where(x => !discoveredById.ContainsKey(x.ModuleId)))
        {
            list.Add(new ModuleCatalogEntry(
                CreateMissingDescriptor(install.ModuleId, install.Version),
                install.Enabled ? ModuleLifecycleState.InstalledEnabled : ModuleLifecycleState.InstalledDisabled,
                Installed: true,
                Enabled: install.Enabled,
                EffectivelyActive: false,
                ValidationIssues: [new ModuleLifecycleBlocker("descriptor_missing", $"Installed module '{install.ModuleId}' is not discoverable by this host.")]));
        }

        return list
            .OrderBy(x => x.Descriptor.ModuleId, StringComparer.Ordinal)
            .ToList();
    }

    public async ValueTask<ModuleCatalogEntry?> GetModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var all = await ListModulesAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(x => string.Equals(x.Descriptor.ModuleId, moduleId, StringComparison.Ordinal));
    }

    public async ValueTask<IReadOnlyList<ModuleDescriptor>> GetEnabledDescriptorsAsync(CancellationToken cancellationToken = default)
    {
        var entries = await ListModulesAsync(cancellationToken).ConfigureAwait(false);
        return entries
            .Where(x => x.EffectivelyActive)
            .Select(x => x.Descriptor)
            .ToList();
    }

    public async ValueTask<ModuleLifecycleTransitionResult> DiscoverModulesAsync(CancellationToken cancellationToken = default)
    {
        await DiscoverDescriptorsAsync(cancellationToken).ConfigureAwait(false);
        var result = new ModuleLifecycleTransitionResult(
            Succeeded: true,
            ModuleId: "*",
            Action: "discover",
            State: ModuleLifecycleState.Discovered,
            Effect: ModuleLifecycleEffect.Immediate,
            Blockers: []);
        await AppendAuditAsync(result, "Discover modules", cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async ValueTask<ModuleLifecycleTransitionResult> InstallModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var descriptor = await RequireDescriptorAsync(moduleId, cancellationToken).ConfigureAwait(false);
        var state = await GetInstallationStateAsync(moduleId, cancellationToken).ConfigureAwait(false);

        if (state is not null)
        {
            var already = new ModuleLifecycleTransitionResult(true, descriptor.ModuleId, "install", state.Enabled ? ModuleLifecycleState.InstalledEnabled : ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate, []);
            await AppendAuditAsync(already, "Install requested for already-installed module", cancellationToken).ConfigureAwait(false);
            return already;
        }

        var installConflicts = await GetEnableConflictBlockersAsync(descriptor, cancellationToken).ConfigureAwait(false);
        if (installConflicts.Count > 0)
        {
            var blocked = new ModuleLifecycleTransitionResult(false, descriptor.ModuleId, "install", ModuleLifecycleState.Discovered, ModuleLifecycleEffect.Immediate, installConflicts);
            await AppendAuditAsync(blocked, "Install blocked by contribution conflicts", cancellationToken).ConfigureAwait(false);
            return blocked;
        }

        _dbContext.ModuleInstallationStates.Add(new ModuleInstallationStateEntity
        {
            EnvironmentId = _environmentContext.EnvironmentId,
            ModuleId = descriptor.ModuleId,
            Version = descriptor.Version,
            Enabled = false,
            InstalledAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var result = new ModuleLifecycleTransitionResult(true, descriptor.ModuleId, "install", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate, []);
        await AppendAuditAsync(result, "Module installed", cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async ValueTask<ModuleLifecycleTransitionResult> EnableModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var descriptor = await RequireDescriptorAsync(moduleId, cancellationToken).ConfigureAwait(false);
        var state = await GetOrCreateInstallationAsync(descriptor, cancellationToken).ConfigureAwait(false);
        var blockers = await GetEnableConflictBlockersAsync(descriptor, cancellationToken).ConfigureAwait(false);
        if (blockers.Count > 0)
        {
            var blocked = new ModuleLifecycleTransitionResult(false, descriptor.ModuleId, "enable", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate, blockers);
            await AppendAuditAsync(blocked, "Enable blocked by contribution conflicts", cancellationToken).ConfigureAwait(false);
            return blocked;
        }

        state.Enabled = true;
        state.Version = descriptor.Version;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var result = new ModuleLifecycleTransitionResult(true, descriptor.ModuleId, "enable", ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart, []);
        await AppendAuditAsync(result, "Module enabled", cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async ValueTask<ModuleLifecycleTransitionResult> DisableModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var descriptor = await RequireDescriptorAsync(moduleId, cancellationToken).ConfigureAwait(false);
        var blockers = await GetDisableOrUninstallBlockersAsync(descriptor, cancellationToken).ConfigureAwait(false);
        if (blockers.Count > 0)
        {
            var blocked = new ModuleLifecycleTransitionResult(false, descriptor.ModuleId, "disable", ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart, blockers);
            await AppendAuditAsync(blocked, "Disable blocked by persisted references", cancellationToken).ConfigureAwait(false);
            return blocked;
        }

        var state = await GetInstallationStateAsync(moduleId, cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            var notInstalled = new ModuleLifecycleTransitionResult(false, descriptor.ModuleId, "disable", ModuleLifecycleState.Discovered, ModuleLifecycleEffect.Immediate, [new("not_installed", "Module is not installed.")]);
            await AppendAuditAsync(notInstalled, "Disable requested for non-installed module", cancellationToken).ConfigureAwait(false);
            return notInstalled;
        }

        state.Enabled = false;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var result = new ModuleLifecycleTransitionResult(true, descriptor.ModuleId, "disable", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.PartialUntilRestart, []);
        await AppendAuditAsync(result, "Module disabled", cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async ValueTask<ModuleLifecycleTransitionResult> UninstallModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var descriptor = await RequireDescriptorAsync(moduleId, cancellationToken).ConfigureAwait(false);
        var state = await GetInstallationStateAsync(moduleId, cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            var notInstalled = new ModuleLifecycleTransitionResult(false, descriptor.ModuleId, "uninstall", ModuleLifecycleState.Discovered, ModuleLifecycleEffect.Immediate, [new("not_installed", "Module is not installed.")]);
            await AppendAuditAsync(notInstalled, "Uninstall requested for non-installed module", cancellationToken).ConfigureAwait(false);
            return notInstalled;
        }

        var blockers = await GetDisableOrUninstallBlockersAsync(descriptor, cancellationToken).ConfigureAwait(false);
        if (blockers.Count > 0)
        {
            var blocked = new ModuleLifecycleTransitionResult(false, descriptor.ModuleId, "uninstall", state.Enabled ? ModuleLifecycleState.InstalledEnabled : ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.RequiresRestart, blockers);
            await AppendAuditAsync(blocked, "Uninstall blocked by persisted references", cancellationToken).ConfigureAwait(false);
            return blocked;
        }

        _dbContext.ModuleInstallationStates.Remove(state);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var result = new ModuleLifecycleTransitionResult(true, descriptor.ModuleId, "uninstall", ModuleLifecycleState.Discovered, ModuleLifecycleEffect.RequiresRestart, []);
        await AppendAuditAsync(result, "Module uninstalled", cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async ValueTask<IReadOnlyList<ModuleDescriptor>> DiscoverDescriptorsAsync(CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<ModuleDescriptor>();
        foreach (var module in _modules)
        {
            var descriptor = await module.BuildDescriptorAsync(cancellationToken).ConfigureAwait(false);
            if (!seen.Add(descriptor.ModuleId))
                throw new InvalidOperationException($"Duplicate module id '{descriptor.ModuleId}' discovered.");
            list.Add(descriptor);
        }

        return list;
    }

    private async ValueTask<ModuleDescriptor> RequireDescriptorAsync(string moduleId, CancellationToken cancellationToken)
    {
        var discovered = await DiscoverDescriptorsAsync(cancellationToken).ConfigureAwait(false);
        var descriptor = discovered.FirstOrDefault(x => string.Equals(x.ModuleId, moduleId, StringComparison.Ordinal));
        return descriptor ?? throw new InvalidOperationException($"Module '{moduleId}' is not discovered.");
    }

    private async ValueTask<Dictionary<string, ModuleInstallationStateEntity>> GetInstallationsByModuleIdAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.ModuleInstallationStates
            .Where(x => x.EnvironmentId == _environmentContext.EnvironmentId)
            .ToDictionaryAsync(x => x.ModuleId, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<ModuleInstallationStateEntity?> GetInstallationStateAsync(string moduleId, CancellationToken cancellationToken)
    {
        return await _dbContext.ModuleInstallationStates
            .FirstOrDefaultAsync(x => x.EnvironmentId == _environmentContext.EnvironmentId && x.ModuleId == moduleId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<ModuleInstallationStateEntity> GetOrCreateInstallationAsync(ModuleDescriptor descriptor, CancellationToken cancellationToken)
    {
        var existing = await GetInstallationStateAsync(descriptor.ModuleId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        var created = new ModuleInstallationStateEntity
        {
            EnvironmentId = _environmentContext.EnvironmentId,
            ModuleId = descriptor.ModuleId,
            Version = descriptor.Version,
            Enabled = false,
            InstalledAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.ModuleInstallationStates.Add(created);
        return created;
    }

    private async ValueTask<IReadOnlyList<ModuleLifecycleBlocker>> GetDisableOrUninstallBlockersAsync(ModuleDescriptor descriptor, CancellationToken cancellationToken)
    {
        var blockers = new List<ModuleLifecycleBlocker>();
        var workflowIds = descriptor.Contributions
            .OfType<WorkflowContributionDescriptor>()
            .Select(x => x.Id)
            .ToArray();
        if (workflowIds.Length > 0)
        {
            var hasWorkflowInstances = await _dbContext.WorkflowInstances.AnyAsync(x => workflowIds.Contains(x.DefinitionId), cancellationToken).ConfigureAwait(false);
            if (hasWorkflowInstances)
                blockers.Add(new("active_workflow_references", "Module contributes workflow definitions referenced by persisted workflow instances."));
        }

        var operationIds = descriptor.Contributions
            .OfType<OperationContributionDescriptor>()
            .Select(x => x.Id)
            .ToArray();
        if (operationIds.Length > 0)
        {
            var hasOperationInstances = await _dbContext.OperationInstances.AnyAsync(x => operationIds.Contains(x.DefinitionId), cancellationToken).ConfigureAwait(false);
            if (hasOperationInstances)
                blockers.Add(new("active_operation_references", "Module contributes operation definitions referenced by persisted operation instances."));
        }

        return blockers;
    }

    private static ModuleCatalogEntry ToCatalogEntry(
        ModuleDescriptor descriptor,
        ModuleInstallationStateEntity? state,
        IReadOnlyDictionary<string, IReadOnlyList<ModuleLifecycleBlocker>> issuesByModule)
    {
        if (state is null)
            return new ModuleCatalogEntry(
                descriptor,
                ModuleLifecycleState.Discovered,
                Installed: false,
                Enabled: false,
                EffectivelyActive: false,
                ValidationIssues: []);

        var issues = issuesByModule.TryGetValue(descriptor.ModuleId, out var mappedIssues)
            ? mappedIssues
            : [];
        var effectivelyActive = state.Enabled && issues.Count == 0;
        return new ModuleCatalogEntry(
            descriptor,
            state.Enabled ? ModuleLifecycleState.InstalledEnabled : ModuleLifecycleState.InstalledDisabled,
            Installed: true,
            Enabled: state.Enabled,
            EffectivelyActive: effectivelyActive,
            ValidationIssues: issues);
    }

    private async ValueTask<IReadOnlyList<ModuleLifecycleBlocker>> GetEnableConflictBlockersAsync(
        ModuleDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var discovered = await DiscoverDescriptorsAsync(cancellationToken).ConfigureAwait(false);
        var installs = await GetInstallationsByModuleIdAsync(cancellationToken).ConfigureAwait(false);

        var candidateInstall = installs.TryGetValue(descriptor.ModuleId, out var existing)
            ? existing
            : new ModuleInstallationStateEntity
            {
                EnvironmentId = _environmentContext.EnvironmentId,
                ModuleId = descriptor.ModuleId,
                Version = descriptor.Version,
                Enabled = true
            };
        candidateInstall.Enabled = true;
        installs[descriptor.ModuleId] = candidateInstall;

        var issues = BuildCrossModuleIssues(discovered, installs, onlyEnabled: true);
        return issues.TryGetValue(descriptor.ModuleId, out var blockers)
            ? blockers
            : [];
    }

    private static Dictionary<string, IReadOnlyList<ModuleLifecycleBlocker>> BuildCrossModuleIssues(
        IReadOnlyList<ModuleDescriptor> discovered,
        IReadOnlyDictionary<string, ModuleInstallationStateEntity> installs,
        bool onlyEnabled)
    {
        var discoveredById = discovered.ToDictionary(x => x.ModuleId, StringComparer.Ordinal);
        var issues = new Dictionary<string, List<ModuleLifecycleBlocker>>(StringComparer.Ordinal);

        foreach (var install in installs.Values)
        {
            if (!discoveredById.ContainsKey(install.ModuleId))
                AddIssue(issues, install.ModuleId, "descriptor_missing", $"Installed module '{install.ModuleId}' is not discoverable by this host.");
        }

        var active = discovered.Where(d =>
        {
            if (!installs.TryGetValue(d.ModuleId, out var install))
                return !onlyEnabled;
            return !onlyEnabled || install.Enabled;
        }).ToList();

        EnforceUnique(
            active,
            issues,
            d => d.Contributions.OfType<WorkflowContributionDescriptor>().Select(c => c.Id),
            "workflow_id_conflict",
            "workflow id");
        EnforceUnique(
            active,
            issues,
            d => d.Contributions.OfType<OperationContributionDescriptor>().Select(c => c.Id),
            "operation_id_conflict",
            "operation id");
        EnforceUnique(
            active,
            issues,
            d => d.Contributions.OfType<NavigationContributionDescriptor>().Select(c => c.Id),
            "navigation_id_conflict",
            "navigation id");
        EnforceUnique(
            active,
            issues,
            d => d.Contributions.OfType<NavigationContributionDescriptor>().Select(c => c.Route),
            "navigation_route_conflict",
            "navigation route");
        EnforceUnique(
            active,
            issues,
            d => d.Contributions.OfType<PermissionContributionDescriptor>().Select(c => $"{c.Action}:{c.ResourceType}"),
            "permission_conflict",
            "permission");

        return issues.ToDictionary(x => x.Key, x => (IReadOnlyList<ModuleLifecycleBlocker>)x.Value, StringComparer.Ordinal);
    }

    private static void EnforceUnique(
        IReadOnlyList<ModuleDescriptor> active,
        IDictionary<string, List<ModuleLifecycleBlocker>> issues,
        Func<ModuleDescriptor, IEnumerable<string>> selector,
        string issueCode,
        string label)
    {
        var ownerByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var descriptor in active)
        {
            foreach (var value in selector(descriptor).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (ownerByKey.TryGetValue(value, out var owner))
                {
                    AddIssue(issues, descriptor.ModuleId, issueCode, $"Conflicting {label} '{value}' already provided by enabled module '{owner}'.");
                    AddIssue(issues, owner, issueCode, $"Conflicting {label} '{value}' also provided by enabled module '{descriptor.ModuleId}'.");
                    continue;
                }
                ownerByKey[value] = descriptor.ModuleId;
            }
        }
    }

    private static void AddIssue(
        IDictionary<string, List<ModuleLifecycleBlocker>> issues,
        string moduleId,
        string code,
        string message)
    {
        if (!issues.TryGetValue(moduleId, out var list))
        {
            list = [];
            issues[moduleId] = list;
        }

        if (list.Any(x => x.Code == code && x.Message == message))
            return;
        list.Add(new ModuleLifecycleBlocker(code, message));
    }

    private static ModuleDescriptor CreateMissingDescriptor(string moduleId, string version)
        => new(moduleId, moduleId, version, "Descriptor is missing from host discovery.", null, null, []);

    private async ValueTask AppendAuditAsync(
        ModuleLifecycleTransitionResult result,
        string details,
        CancellationToken cancellationToken)
    {
        var actor = _principalContext is null
            ? null
            : await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        var actorId = actor?.Id ?? new ActorId("system:module-lifecycle");
        var blockerSummary = result.Blockers.Count == 0
            ? "none"
            : string.Join("|", result.Blockers.Select(x => $"{x.Code}:{x.Message}"));

        await _auditService.AppendAsync(
            new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")),
                actorId,
                $"module.lifecycle.{result.Action}",
                new ResourceDescriptor(ResourceTypeConstants.Module, ScopeKeyConstants.Instance),
                result.Succeeded ? "succeeded" : "blocked",
                DateTimeOffset.UtcNow,
                new CorrelationId(null),
                $"{details}; module={result.ModuleId}; state={result.State}; effect={result.Effect}; blockers={blockerSummary}",
                ModuleId: result.ModuleId),
            cancellationToken).ConfigureAwait(false);
    }
}
