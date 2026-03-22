using System.Text.Json;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Scheduling;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Queries runtime health and diagnostics for operator visibility. Epic 30.
/// Aggregates composition, schedule state, blocked deferred work, and anomalies from real platform truth.
/// </summary>
public sealed class RuntimeHealthQueryService : IRuntimeHealthQueryService
{
    private readonly IRuntimeModuleCompositionQueryService _composition;
    private readonly IModuleRegistryService _moduleRegistry;
    private readonly IScheduleManagementService _scheduleManagement;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly ISchedulerClaimOptions _claimOptions;
    private readonly IRunKindResolver _runKinds;
    private readonly AppertureDbContext? _db;

    public RuntimeHealthQueryService(
        IRuntimeModuleCompositionQueryService composition,
        IModuleRegistryService moduleRegistry,
        IScheduleManagementService scheduleManagement,
        IScheduleRepository scheduleRepo,
        ISchedulerClaimOptions claimOptions,
        IRunKindResolver runKinds,
        AppertureDbContext? db = null)
    {
        _composition = composition;
        _moduleRegistry = moduleRegistry;
        _scheduleManagement = scheduleManagement;
        _scheduleRepo = scheduleRepo;
        _claimOptions = claimOptions;
        _runKinds = runKinds;
        _db = db;
    }

    public async ValueTask<RuntimeHealthSnapshot> GetSnapshotAsync(
        string? runtimePlaneId = "local",
        int blockedDeferredWindowHours = 24,
        int scheduleFailureWindowHours = 24,
        CancellationToken cancellationToken = default)
    {
        var planeId = new RuntimePlaneId(runtimePlaneId ?? "local");
        var capturedAt = DateTimeOffset.UtcNow;
        var blockedFrom = capturedAt.AddHours(-blockedDeferredWindowHours);
        var scheduleFrom = capturedAt.AddHours(-scheduleFailureWindowHours);

        var composition = await _composition.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var catalog = await _moduleRegistry.ListModulesAsync(cancellationToken).ConfigureAwait(false);
        var definitions = await _scheduleManagement.ListDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var assignments = await _scheduleRepo.GetAssignmentsAsync(planeId, cancellationToken).ConfigureAwait(false);

        var definitionToModule = BuildDefinitionToModuleMap(catalog);
        var moduleDiagnostics = await BuildModuleDiagnosticsAsync(
            composition, catalog, definitionToModule, planeId, scheduleFrom, blockedFrom, cancellationToken).ConfigureAwait(false);
        var scheduleDiagnostics = await BuildScheduleDiagnosticsAsync(
            definitions, assignments, composition, definitionToModule, cancellationToken).ConfigureAwait(false);
        var blockedDeferred = await GetBlockedDeferredWorkAsync(blockedFrom, definitionToModule, cancellationToken).ConfigureAwait(false);
        var anomalies = BuildAnomalies(composition, scheduleDiagnostics, blockedDeferred);

        var degradationReasons = ComputeDegradationReasons(composition, scheduleDiagnostics, blockedDeferred, anomalies);
        var overallStatus = ComputeOverallStatus(degradationReasons);

        return new RuntimeHealthSnapshot(
            CapturedAtUtc: capturedAt,
            OverallStatus: overallStatus,
            DegradationReasons: degradationReasons,
            ModuleDiagnostics: moduleDiagnostics,
            ScheduleDiagnostics: scheduleDiagnostics,
            BlockedDeferredWork: blockedDeferred,
            Anomalies: anomalies,
            RestartBoundSurfaces: composition.RestartBoundExecutionSurfaces,
            NotYetImplementedSurfaces: composition.NotYetImplementedExecutionSurfaces);
    }

    private static IReadOnlyDictionary<string, string> BuildDefinitionToModuleMap(IReadOnlyList<ModuleCatalogEntry> catalog)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in catalog)
        {
            var moduleId = entry.Descriptor.ModuleId;
            foreach (var c in entry.Descriptor.Contributions.OfType<WorkflowContributionDescriptor>())
                map[c.Id] = moduleId;
            foreach (var c in entry.Descriptor.Contributions.OfType<OperationContributionDescriptor>())
                map[c.Id] = moduleId;
        }
        return map;
    }

    private async Task<IReadOnlyList<ModuleDiagnosticEntry>> BuildModuleDiagnosticsAsync(
        HostEffectiveModuleSnapshot composition,
        IReadOnlyList<ModuleCatalogEntry> catalog,
        IReadOnlyDictionary<string, string> definitionToModule,
        RuntimePlaneId planeId,
        DateTimeOffset scheduleFrom,
        DateTimeOffset blockedFrom,
        CancellationToken ct)
    {
        var blockedByModule = _db != null
            ? await GetBlockedDeferredCountByModuleAsync(blockedFrom, definitionToModule, ct).ConfigureAwait(false)
            : new Dictionary<string, int>(StringComparer.Ordinal);
        var blockedScheduleByModule = _db != null
            ? await GetBlockedScheduleCountByModuleAsync(planeId, scheduleFrom, definitionToModule, ct).ConfigureAwait(false)
            : new Dictionary<string, int>(StringComparer.Ordinal);

        var result = new List<ModuleDiagnosticEntry>();
        foreach (var m in composition.Modules)
        {
            result.Add(new ModuleDiagnosticEntry(
                ModuleId: m.ModuleId,
                LifecycleState: m.LifecycleState,
                PersistedEnabled: m.PersistedEnabled,
                EffectivelyExecutable: m.EffectivelyExecutable,
                AllowsNewStarts: m.AllowsNewStarts,
                HasInFlightWork: m.HasInFlightWork,
                ValidationIssueCodes: m.ValidationIssueCodes,
                ExecutionDegradedReason: m.ExecutionDegradedReason,
                BlockedDeferredWorkCount: blockedByModule.TryGetValue(m.ModuleId, out var bd) && bd > 0 ? bd : null,
                BlockedScheduleCount: blockedScheduleByModule.TryGetValue(m.ModuleId, out var bs) && bs > 0 ? bs : null));
        }
        return result;
    }

    private async Task<Dictionary<string, int>> GetBlockedDeferredCountByModuleAsync(
        DateTimeOffset from,
        IReadOnlyDictionary<string, string> definitionToModule,
        CancellationToken ct)
    {
        if (_db is null) return new Dictionary<string, int>(StringComparer.Ordinal);
        // Contract: RunFailureCapture.DeferredWorkBlockedPrefix is the canonical signal for blocked deferred work.
        var prefix = RunFailureCapture.DeferredWorkBlockedPrefix;
        var runs = await _db.OperationRuns.AsNoTracking()
            .Where(r =>
                r.Status == WorkflowLifecycleStatusNames.Failed &&
                r.CompletedAt >= from &&
                r.FailureSummary != null &&
                r.FailureSummary.StartsWith(prefix))
            .Select(r => new { r.DefinitionId })
            .ToListAsync(ct).ConfigureAwait(false);
        var byModule = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in runs)
        {
            if (definitionToModule.TryGetValue(r.DefinitionId, out var moduleId))
            {
                byModule.TryGetValue(moduleId, out var c);
                byModule[moduleId] = c + 1;
            }
        }
        return byModule;
    }

    private async Task<Dictionary<string, int>> GetBlockedScheduleCountByModuleAsync(
        RuntimePlaneId planeId,
        DateTimeOffset from,
        IReadOnlyDictionary<string, string> definitionToModule,
        CancellationToken ct)
    {
        if (_db is null) return new Dictionary<string, int>(StringComparer.Ordinal);
        var assignments = await _scheduleRepo.GetAssignmentsAsync(planeId, ct).ConfigureAwait(false);
        var definitionIds = new HashSet<string>(StringComparer.Ordinal);
        var definitions = await _scheduleManagement.ListDefinitionsAsync(ct).ConfigureAwait(false);
        foreach (var d in definitions)
        {
            var defId = ParseTargetDefinitionId(d.Target.JobType, d.Target.JobConfiguration);
            if (defId != null) definitionIds.Add(defId);
        }
        var runs = await _db.ScheduleRuns.AsNoTracking()
            .Where(r =>
                r.CompletedAtUtc >= from &&
                (r.Outcome == "BlockedByLifecycle" || r.Outcome == "Failed"))
            .Select(r => new { r.ScheduleId, r.Outcome })
            .ToListAsync(ct).ConfigureAwait(false);
        var scheduleToDefId = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var d in definitions)
        {
            scheduleToDefId[d.Id.Value] = ParseTargetDefinitionId(d.Target.JobType, d.Target.JobConfiguration);
        }
        var byModule = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in runs)
        {
            if (scheduleToDefId.TryGetValue(r.ScheduleId, out var defId) && defId != null &&
                definitionToModule.TryGetValue(defId, out var moduleId))
            {
                byModule.TryGetValue(moduleId, out var c);
                byModule[moduleId] = c + 1;
            }
        }
        return byModule;
    }

    private static string? ParseTargetDefinitionId(ScheduledJobType jobType, string? jobConfiguration)
    {
        if (string.IsNullOrWhiteSpace(jobConfiguration)) return null;
        try
        {
            var doc = JsonDocument.Parse(jobConfiguration);
            if (jobType == ScheduledJobType.WorkflowExecution && doc.RootElement.TryGetProperty("workflowDefinitionId", out var wf))
                return wf.GetString();
            if (jobType == ScheduledJobType.ModuleOperationExecution && doc.RootElement.TryGetProperty("operationDefinitionId", out var op))
                return op.GetString();
        }
        catch { }
        return null;
    }

    private async Task<IReadOnlyList<ScheduleDiagnosticEntry>> BuildScheduleDiagnosticsAsync(
        IReadOnlyList<ScheduleDefinition> definitions,
        IReadOnlyList<ScheduleAssignment> assignments,
        HostEffectiveModuleSnapshot composition,
        IReadOnlyDictionary<string, string> definitionToModule,
        CancellationToken ct)
    {
        var result = new List<ScheduleDiagnosticEntry>();
        var effectivelyExecutable = composition.EffectivelyExecutableModuleIds.ToHashSet(StringComparer.Ordinal);
        foreach (var def in definitions)
        {
            var assignment = assignments.FirstOrDefault(a => a.ScheduleId.Value == def.Id.Value);
            ScheduleState? state = null;
            if (assignment != null)
                state = await _scheduleRepo.GetStateAsync(assignment.Id.Value, ct).ConfigureAwait(false);
            var targetDefId = ParseTargetDefinitionId(def.Target.JobType, def.Target.JobConfiguration);
            var targetModuleExecutable = targetDefId != null && definitionToModule.TryGetValue(targetDefId, out var moduleId)
                && effectivelyExecutable.Contains(moduleId);
            var (scheduleHealth, degradedReason) = ComputeScheduleHealth(def, state, targetModuleExecutable);
            string? claimedByHostId = null;
            DateTimeOffset? claimedAtUtc = null;
            bool? isClaimStale = null;
            if (assignment != null)
            {
                var claimInfo = await _scheduleRepo.GetInProgressRunClaimAsync(assignment.Id.Value, ct).ConfigureAwait(false);
                if (claimInfo.HasValue)
                {
                    claimedByHostId = string.IsNullOrEmpty(claimInfo.Value.ClaimedByHostId) ? null : claimInfo.Value.ClaimedByHostId;
                    claimedAtUtc = claimInfo.Value.StartedAtUtc;
                    var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_claimOptions.ClaimLeaseMinutes);
                    isClaimStale = claimInfo.Value.StartedAtUtc < cutoff;
                }
            }
            result.Add(new ScheduleDiagnosticEntry(
                ScheduleId: def.Id.Value,
                ScheduleName: def.Name,
                Enabled: def.Enabled,
                TargetJobType: (int)def.Target.JobType,
                TargetJobConfiguration: def.Target.JobConfiguration,
                NextDueUtc: state?.NextDueUtc,
                LastAttemptedUtc: state?.LastAttemptedUtc,
                LastOutcome: state?.LastOutcome,
                LastOutcomeReason: state?.LastOutcomeReason,
                LastSuccessfulDispatchUtc: state?.LastSuccessfulDispatchUtc,
                ScheduleHealthStatus: scheduleHealth,
                DegradedReason: degradedReason,
                TargetModuleExecutable: targetModuleExecutable,
                ClaimedByHostId: claimedByHostId,
                ClaimedAtUtc: claimedAtUtc,
                IsClaimStale: isClaimStale));
        }
        return result;
    }

    private static (string Status, string? Reason) ComputeScheduleHealth(
        ScheduleDefinition def,
        ScheduleState? state,
        bool targetModuleExecutable)
    {
        if (!def.Enabled)
            return (ScheduleHealthStatus.Unknown, null);
        if (!targetModuleExecutable)
            return (ScheduleHealthStatus.Blocked, "Target module not executable for new work");
        var outcome = state?.LastOutcome;
        if (outcome == "BlockedByLifecycle")
            return (ScheduleHealthStatus.Blocked, state?.LastOutcomeReason ?? "Module disabled or definition not executable");
        if (outcome == "Failed")
            return (ScheduleHealthStatus.Degraded, state?.LastOutcomeReason ?? "Last dispatch failed");
        if (outcome == "Dispatched")
            return (ScheduleHealthStatus.Healthy, null);
        return (ScheduleHealthStatus.Unknown, null);
    }

    private async Task<IReadOnlyList<BlockedDeferredWorkEntry>> GetBlockedDeferredWorkAsync(
        DateTimeOffset from,
        IReadOnlyDictionary<string, string> definitionToModule,
        CancellationToken ct)
    {
        if (_db is null)
            return Array.Empty<BlockedDeferredWorkEntry>();
        // Contract: RunFailureCapture.DeferredWorkBlockedPrefix; see RunFailureCapture docs.
        var prefix = RunFailureCapture.DeferredWorkBlockedPrefix;
        var runs = await _db.OperationRuns.AsNoTracking()
            .Where(r =>
                r.Status == WorkflowLifecycleStatusNames.Failed &&
                r.CompletedAt >= from &&
                r.FailureSummary != null &&
                r.FailureSummary.StartsWith(prefix))
            .OrderByDescending(r => r.CompletedAt)
            .Take(50)
            .Select(r => new { r.Id, r.DefinitionId, r.FailureSummary, r.CompletedAt })
            .ToListAsync(ct).ConfigureAwait(false);
        var runIds = runs.Select(r => r.Id).ToList();
        var runKinds = await _runKinds.ResolveRunKindsAsync(runIds, ct).ConfigureAwait(false);
        var result = new List<BlockedDeferredWorkEntry>();
        foreach (var r in runs)
        {
            var kind = runKinds.TryGetValue(r.Id, out var k) ? k : "unknown";
            var moduleId = definitionToModule.TryGetValue(r.DefinitionId, out var mid) ? mid : null;
            result.Add(new BlockedDeferredWorkEntry(
                RunId: r.Id,
                DefinitionId: r.DefinitionId,
                DefinitionKind: kind,
                ModuleId: moduleId,
                FailedAtUtc: r.CompletedAt ?? DateTimeOffset.MinValue,
                Summary: r.FailureSummary ?? "[Blocked] Unknown"));
        }
        return result;
    }

    private static IReadOnlyList<ExecutionAnomalyEntry> BuildAnomalies(
        HostEffectiveModuleSnapshot composition,
        IReadOnlyList<ScheduleDiagnosticEntry> scheduleDiagnostics,
        IReadOnlyList<BlockedDeferredWorkEntry> blockedDeferred)
    {
        var anomalies = new List<ExecutionAnomalyEntry>();
        foreach (var m in composition.Modules)
        {
            if (m.ExecutionDegradedReason == "invalid_persisted_state")
            {
                anomalies.Add(new ExecutionAnomalyEntry(
                    AnomalyKind: "module_invalid_persisted_state",
                    Severity: "warning",
                    ModuleId: m.ModuleId,
                    ScheduleId: null,
                    RunId: null,
                    DefinitionId: null,
                    Summary: $"Module {m.ModuleId} is enabled in persistence but not effectively executable (validation issues).",
                    OccurredAtUtc: DateTimeOffset.UtcNow));
            }
        }
        foreach (var s in scheduleDiagnostics.Where(x => x.ScheduleHealthStatus == ScheduleHealthStatus.Blocked || x.ScheduleHealthStatus == ScheduleHealthStatus.Degraded))
        {
            anomalies.Add(new ExecutionAnomalyEntry(
                AnomalyKind: s.ScheduleHealthStatus == ScheduleHealthStatus.Blocked ? "schedule_blocked" : "schedule_degraded",
                Severity: s.ScheduleHealthStatus == ScheduleHealthStatus.Blocked ? "high" : "warning",
                ModuleId: null,
                ScheduleId: s.ScheduleId,
                RunId: null,
                DefinitionId: null,
                Summary: $"Schedule {s.ScheduleName}: {s.DegradedReason ?? s.LastOutcomeReason ?? s.ScheduleHealthStatus}",
                OccurredAtUtc: s.LastAttemptedUtc ?? DateTimeOffset.UtcNow));
        }
        foreach (var b in blockedDeferred.Take(10))
        {
            anomalies.Add(new ExecutionAnomalyEntry(
                AnomalyKind: "deferred_work_blocked",
                Severity: "warning",
                ModuleId: b.ModuleId,
                ScheduleId: null,
                RunId: b.RunId,
                DefinitionId: b.DefinitionId,
                Summary: b.Summary,
                OccurredAtUtc: b.FailedAtUtc));
        }
        return anomalies;
    }

    private static IReadOnlyList<string> ComputeDegradationReasons(
        HostEffectiveModuleSnapshot composition,
        IReadOnlyList<ScheduleDiagnosticEntry> scheduleDiagnostics,
        IReadOnlyList<BlockedDeferredWorkEntry> blockedDeferred,
        IReadOnlyList<ExecutionAnomalyEntry> anomalies)
    {
        var reasons = new List<string>();
        var degradedModules = composition.Modules.Count(m => m.ExecutionDegradedReason != null);
        if (degradedModules > 0)
            reasons.Add($"{degradedModules} module(s) degraded or not executable for new work");
        var blockedSchedules = scheduleDiagnostics.Count(s => s.ScheduleHealthStatus == ScheduleHealthStatus.Blocked);
        if (blockedSchedules > 0)
            reasons.Add($"{blockedSchedules} schedule(s) blocked by lifecycle or target not executable");
        var failedSchedules = scheduleDiagnostics.Count(s => s.ScheduleHealthStatus == ScheduleHealthStatus.Degraded);
        if (failedSchedules > 0)
            reasons.Add($"{failedSchedules} schedule(s) with recent failed dispatch");
        if (blockedDeferred.Count > 0)
            reasons.Add($"{blockedDeferred.Count} deferred run(s) blocked by lifecycle in window");
        var invalidModules = composition.Modules.Count(m => m.ExecutionDegradedReason == "invalid_persisted_state");
        if (invalidModules > 0)
            reasons.Add($"{invalidModules} module(s) with invalid persisted enabled state");
        return reasons;
    }

    private static string ComputeOverallStatus(IReadOnlyList<string> degradationReasons)
    {
        if (degradationReasons.Count == 0)
            return RuntimeHealthStatus.Healthy;
        return RuntimeHealthStatus.Degraded;
    }
}
