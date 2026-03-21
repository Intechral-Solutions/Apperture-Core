namespace Apperture.Core.Application.RuntimeHealth;

/// <summary>
/// Queries runtime health and diagnostics for operator visibility. Epic 30.
/// </summary>
public interface IRuntimeHealthQueryService
{
    /// <summary>
    /// Gets a unified runtime health snapshot including module, schedule, blocked work, and anomaly diagnostics.
    /// </summary>
    /// <param name="runtimePlaneId">Runtime plane for schedule diagnostics (default "local").</param>
    /// <param name="blockedDeferredWindowHours">Hours to look back for blocked deferred work (default 24).</param>
    /// <param name="scheduleFailureWindowHours">Hours to look back for repeated schedule failures (default 24).</param>
    ValueTask<RuntimeHealthSnapshot> GetSnapshotAsync(
        string? runtimePlaneId = "local",
        int blockedDeferredWindowHours = 24,
        int scheduleFailureWindowHours = 24,
        CancellationToken cancellationToken = default);
}
