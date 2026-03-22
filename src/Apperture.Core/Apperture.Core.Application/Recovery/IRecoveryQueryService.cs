namespace Apperture.Core.Application.Recovery;

/// <summary>
/// Queries operator recovery and remediation context. Epic 31.
/// </summary>
public interface IRecoveryQueryService
{
    /// <summary>
    /// Gets blocked deferred work with remediation eligibility.
    /// </summary>
    ValueTask<IReadOnlyList<BlockedWorkRemediationEntry>> GetBlockedWorkRemediationAsync(
        string? runtimePlaneId = "local",
        int windowHours = 24,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single blocked item's remediation context, or null if not found/not blocked.
    /// </summary>
    ValueTask<BlockedWorkRemediationEntry?> GetBlockedWorkRemediationForRunAsync(
        string runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets schedule remediation context.
    /// </summary>
    ValueTask<ScheduleRemediationEntry?> GetScheduleRemediationAsync(
        string scheduleId,
        string? runtimePlaneId = "local",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets module remediation context.
    /// </summary>
    ValueTask<ModuleRemediationEntry?> GetModuleRemediationAsync(
        string moduleId,
        CancellationToken cancellationToken = default);
}
