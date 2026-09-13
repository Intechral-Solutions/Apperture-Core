using Apperture.Core.Application.Recovery;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Scheduling;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Queries operator recovery and remediation context. Epic 31.
/// Projects eligibility from runtime composition truth; does not fabricate.
/// </summary>
public sealed class RecoveryQueryService : IRecoveryQueryService
{
    private const string OperationRetryNotSupportedReason =
        "Operation-level retry is not supported. Recover via Affected records (item retry or acknowledgement).";
    private const string ModuleNotExecutableReason = "Module not executable for new work";

    private readonly IRuntimeHealthQueryService _health;

    public RecoveryQueryService(IRuntimeHealthQueryService health)
    {
        _health = health;
    }

    public async ValueTask<IReadOnlyList<BlockedWorkRemediationEntry>> GetBlockedWorkRemediationAsync(
        string? runtimePlaneId = "local",
        int windowHours = 24,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _health.GetSnapshotAsync(runtimePlaneId, windowHours, windowHours, cancellationToken).ConfigureAwait(false);
        var executableModuleIds = snapshot.ModuleDiagnostics
            .Where(m => m.EffectivelyExecutable)
            .Select(m => m.ModuleId)
            .ToHashSet(StringComparer.Ordinal);

        return snapshot.BlockedDeferredWork
            .Select(b => ToRemediationEntry(b, executableModuleIds))
            .ToList();
    }

    public async ValueTask<BlockedWorkRemediationEntry?> GetBlockedWorkRemediationForRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        var list = await GetBlockedWorkRemediationAsync("local", 168, cancellationToken).ConfigureAwait(false);
        return list.FirstOrDefault(b => string.Equals(b.RunId, runId, StringComparison.Ordinal));
    }

    public async ValueTask<ScheduleRemediationEntry?> GetScheduleRemediationAsync(
        string scheduleId,
        string? runtimePlaneId = "local",
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _health.GetSnapshotAsync(runtimePlaneId, 24, 24, cancellationToken).ConfigureAwait(false);
        var schedule = snapshot.ScheduleDiagnostics.FirstOrDefault(s =>
            string.Equals(s.ScheduleId, scheduleId, StringComparison.Ordinal));
        if (schedule is null)
            return null;

        var remediationAvailable = schedule.Enabled && schedule.TargetModuleExecutable;
        var nextCycleProcessesAt = schedule.NextDueUtc.HasValue
            ? $"Next due at {schedule.NextDueUtc:O}; run cycle processes schedules due at cycle time."
            : null;

        return new ScheduleRemediationEntry(
            ScheduleId: schedule.ScheduleId,
            ScheduleName: schedule.ScheduleName,
            Enabled: schedule.Enabled,
            LastOutcome: schedule.LastOutcome,
            LastOutcomeReason: schedule.LastOutcomeReason,
            TargetModuleExecutable: schedule.TargetModuleExecutable,
            RemediationAvailable: remediationAvailable,
            NextDueUtc: schedule.NextDueUtc,
            NextCycleProcessesAt: nextCycleProcessesAt);
    }

    public async ValueTask<ModuleRemediationEntry?> GetModuleRemediationAsync(
        string moduleId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _health.GetSnapshotAsync("local", 24, 24, cancellationToken).ConfigureAwait(false);
        var module = snapshot.ModuleDiagnostics.FirstOrDefault(m =>
            string.Equals(m.ModuleId, moduleId, StringComparison.Ordinal));
        if (module is null)
            return null;

        var prerequisiteHint = BuildPrerequisiteHint(module);
        return new ModuleRemediationEntry(
            ModuleId: module.ModuleId,
            LifecycleState: module.LifecycleState,
            EffectivelyExecutable: module.EffectivelyExecutable,
            ExecutionDegradedReason: module.ExecutionDegradedReason,
            BlockedDeferredWorkCount: module.BlockedDeferredWorkCount,
            BlockedScheduleCount: module.BlockedScheduleCount,
            PrerequisiteHint: prerequisiteHint);
    }

    private static BlockedWorkRemediationEntry ToRemediationEntry(
        BlockedDeferredWorkEntry b,
        HashSet<string> executableModuleIds)
    {
        bool retryEligibleNow;
        string? retryEligibleReason;

        if (string.Equals(b.DefinitionKind, "operation", StringComparison.OrdinalIgnoreCase))
        {
            retryEligibleNow = false;
            retryEligibleReason = OperationRetryNotSupportedReason;
        }
        else if (b.ModuleId is null)
        {
            retryEligibleNow = false;
            retryEligibleReason = "Definition not mapped to module; cannot determine eligibility.";
        }
        else if (executableModuleIds.Contains(b.ModuleId))
        {
            retryEligibleNow = true;
            retryEligibleReason = "Module executable; retry eligible.";
        }
        else
        {
            retryEligibleNow = false;
            retryEligibleReason = $"{ModuleNotExecutableReason} (module {b.ModuleId}).";
        }

        return new BlockedWorkRemediationEntry(
            RunId: b.RunId,
            DefinitionId: b.DefinitionId,
            DefinitionKind: b.DefinitionKind,
            ModuleId: b.ModuleId,
            FailedAtUtc: b.FailedAtUtc,
            BlockedReason: b.Summary,
            RetryEligibleNow: retryEligibleNow,
            RetryEligibleReason: retryEligibleReason);
    }

    private static string? BuildPrerequisiteHint(ModuleDiagnosticEntry m)
    {
        if (m.EffectivelyExecutable)
            return null;

        if (m.ExecutionDegradedReason == "invalid_persisted_state" && m.ValidationIssueCodes.Count > 0)
            return $"Resolve validation: {string.Join(", ", m.ValidationIssueCodes)}";

        if (m.PersistedEnabled && !m.EffectivelyExecutable)
            return "Module enabled in persistence but not executable; resolve validation or configuration.";

        if (!m.PersistedEnabled)
            return "Enable module to allow new starts and unblock deferred work.";

        return "Resolve module degradation before remediation.";
    }
}
