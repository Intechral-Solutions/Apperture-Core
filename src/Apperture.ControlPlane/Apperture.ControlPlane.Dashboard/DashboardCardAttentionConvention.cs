using Apperture.Core.Application.RuntimeHealth;

namespace Apperture.ControlPlane.Dashboard;

/// <summary>
/// Shared mapping from platform signals to <see cref="DashboardCardAttention"/> for a coherent operator dashboard (Epic 46).
/// </summary>
public static class DashboardCardAttentionConvention
{
    /// <summary>Maps runtime health overall status (Epic 30 vocabulary).</summary>
    public static DashboardCardAttention FromRuntimeHealthOverallStatus(string overallStatus)
    {
        if (string.Equals(overallStatus, RuntimeHealthStatus.Unhealthy, StringComparison.OrdinalIgnoreCase))
            return DashboardCardAttention.Critical;
        if (string.Equals(overallStatus, RuntimeHealthStatus.Degraded, StringComparison.OrdinalIgnoreCase))
            return DashboardCardAttention.Warning;
        return DashboardCardAttention.None;
    }

    /// <summary>Maps control attention severity tokens (e.g. high / medium / low).</summary>
    public static DashboardCardAttention FromAttentionSeverityToken(string? rawSeverity)
    {
        var raw = rawSeverity?.Trim() ?? "";
        if (raw.Equals("high", StringComparison.OrdinalIgnoreCase))
            return DashboardCardAttention.Critical;
        if (raw.Equals("medium", StringComparison.OrdinalIgnoreCase))
            return DashboardCardAttention.Warning;
        if (raw.Equals("low", StringComparison.OrdinalIgnoreCase))
            return DashboardCardAttention.Info;
        return DashboardCardAttention.None;
    }

    /// <summary>
    /// Picks the worst severity token across attention items (list order may be newest-first).
    /// Returns a normalized token: high, medium, low, or none.
    /// </summary>
    public static string MaxAttentionSeverityToken(IEnumerable<string?> severities)
    {
        var bestRank = -1;
        string? bestToken = null;
        foreach (var s in severities)
        {
            var rank = AttentionSeverityRank(s);
            if (rank > bestRank)
            {
                bestRank = rank;
                bestToken = NormalizeAttentionSeverityToken(s);
            }
        }

        return bestRank <= 0 ? "none" : bestToken ?? "none";
    }

    private static int AttentionSeverityRank(string? rawSeverity)
    {
        var raw = rawSeverity?.Trim() ?? "";
        if (raw.Equals("high", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (raw.Equals("medium", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (raw.Equals("low", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 0;
    }

    private static string? NormalizeAttentionSeverityToken(string? rawSeverity)
    {
        var raw = rawSeverity?.Trim() ?? "";
        if (raw.Equals("high", StringComparison.OrdinalIgnoreCase))
            return "high";
        if (raw.Equals("medium", StringComparison.OrdinalIgnoreCase))
            return "medium";
        if (raw.Equals("low", StringComparison.OrdinalIgnoreCase))
            return "low";
        return string.IsNullOrEmpty(raw) ? null : raw;
    }

    /// <summary>
    /// Schedules card: surface warning when runtime diagnostics show degraded/blocked schedules.
    /// </summary>
    public static DashboardCardAttention FromScheduleDefinitionCountsAndHealth(
        int totalDefinitions,
        int enabledCount,
        int degradedOrBlockedScheduleCount)
    {
        if (degradedOrBlockedScheduleCount > 0)
            return DashboardCardAttention.Warning;
        return FromScheduleDefinitionCounts(totalDefinitions, enabledCount);
    }

    /// <summary>Authentication posture: lockout risk is critical; other warnings stay at warning.</summary>
    public static DashboardCardAttention FromAuthPosture(bool noViableSignIn, int warningCount)
    {
        if (noViableSignIn)
            return DashboardCardAttention.Critical;
        return warningCount > 0 ? DashboardCardAttention.Warning : DashboardCardAttention.None;
    }

    /// <summary>Execution snapshot: failures warrant warning; avoids critical overlap with the Needs Attention card.</summary>
    public static DashboardCardAttention FromVisibleFailedRunsCount(int failedCount) =>
        failedCount > 0 ? DashboardCardAttention.Warning : DashboardCardAttention.None;

    /// <summary>Schedules definitions-only card: informational when definitions exist.</summary>
    public static DashboardCardAttention FromScheduleDefinitionCounts(int totalDefinitions, int enabledCount)
    {
        if (totalDefinitions == 0)
            return DashboardCardAttention.None;
        return enabledCount == 0 ? DashboardCardAttention.Warning : DashboardCardAttention.Info;
    }

    /// <summary>Audit volume in the window: elevated activity is informational.</summary>
    public static DashboardCardAttention FromAuditEventCount(int countLastWindow) =>
        countLastWindow > 0 ? DashboardCardAttention.Info : DashboardCardAttention.None;

    /// <summary>Governance catalog snapshot: neutral unless there are no roles (unexpected).</summary>
    public static DashboardCardAttention FromRoleCount(int roleCount) =>
        roleCount == 0 ? DashboardCardAttention.Warning : DashboardCardAttention.None;

    /// <summary>Blocked deferred work awaiting remediation.</summary>
    public static DashboardCardAttention FromBlockedWorkCount(int blockedCount) =>
        blockedCount > 0 ? DashboardCardAttention.Warning : DashboardCardAttention.None;

    /// <summary>Integration fleet: blocking readiness or operational silence warrant operator review.</summary>
    public static DashboardCardAttention FromIntegrationFleetSignals(
        int blockingCount,
        int operationalFailureCount,
        int unexpectedSilenceCount,
        int notRunnableCount)
    {
        if (blockingCount > 0 || unexpectedSilenceCount > 0)
            return DashboardCardAttention.Critical;
        if (operationalFailureCount > 0 || notRunnableCount > 0)
            return DashboardCardAttention.Warning;
        return DashboardCardAttention.None;
    }
}
