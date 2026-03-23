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
}
