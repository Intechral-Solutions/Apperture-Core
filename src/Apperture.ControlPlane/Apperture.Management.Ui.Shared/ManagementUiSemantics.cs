namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Epic 79: semantic status and attention tokens for Management UI primitives.
/// Display strings still come from APIs; this maps known tokens to visual roles.
/// </summary>
public enum ManagementStatusSemantic
{
    Success,
    Warning,
    Error,
    Blocking,
    Neutral,
    Unknown,
    Pending,
    Forbidden
}

public enum ManagementAttentionSemantic
{
    Critical,
    Warning,
    Info,
    None
}

public static class ManagementUiSemantics
{
    public static string GetStatusPillClass(ManagementStatusSemantic semantic) => semantic switch
    {
        ManagementStatusSemantic.Success => "ui-status-pill ui-status-pill--success",
        ManagementStatusSemantic.Warning => "ui-status-pill ui-status-pill--warning",
        ManagementStatusSemantic.Error => "ui-status-pill ui-status-pill--error",
        ManagementStatusSemantic.Blocking => "ui-status-pill ui-status-pill--error",
        ManagementStatusSemantic.Pending => "ui-status-pill ui-status-pill--pending",
        ManagementStatusSemantic.Forbidden => "ui-status-pill ui-status-pill--forbidden",
        ManagementStatusSemantic.Unknown => "ui-status-pill ui-status-pill--unknown",
        _ => "ui-status-pill ui-status-pill--neutral"
    };

    public static string GetAttentionPillClass(ManagementAttentionSemantic semantic) => semantic switch
    {
        ManagementAttentionSemantic.Critical => "ui-attention-pill ui-attention-pill--critical",
        ManagementAttentionSemantic.Warning => "ui-attention-pill ui-attention-pill--warning",
        ManagementAttentionSemantic.Info => "ui-attention-pill ui-attention-pill--info",
        _ => "ui-attention-pill ui-attention-pill--neutral"
    };

    /// <summary>Maps dashboard card attention tokens (Epic 45–47).</summary>
    public static ManagementAttentionSemantic MapDashboardAttention(string? attention)
    {
        if (string.IsNullOrWhiteSpace(attention))
            return ManagementAttentionSemantic.None;

        return attention.Trim().ToLowerInvariant() switch
        {
            "critical" => ManagementAttentionSemantic.Critical,
            "warning" => ManagementAttentionSemantic.Warning,
            "info" => ManagementAttentionSemantic.Info,
            _ => ManagementAttentionSemantic.None
        };
    }

    public static string GetDashboardAttentionPillClass(string? attention) =>
        GetAttentionPillClass(MapDashboardAttention(attention));

    /// <summary>Runtime health overall status strings from platform health APIs.</summary>
    public static ManagementStatusSemantic MapRuntimeHealthStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return ManagementStatusSemantic.Unknown;

        return status.Trim() switch
        {
            "Healthy" => ManagementStatusSemantic.Success,
            "Degraded" => ManagementStatusSemantic.Warning,
            _ => ManagementStatusSemantic.Error
        };
    }

    public static string GetRuntimeHealthStatusPillClass(string? status) =>
        GetStatusPillClass(MapRuntimeHealthStatus(status));

    /// <summary>
    /// Inline text color for legacy pages; prefer <see cref="GetRuntimeHealthStatusPillClass"/> for new work.
    /// Delegates to <see cref="Epic33UiDisplayHelpers.GetStatusCssClass"/>.
    /// </summary>
    public static string GetRuntimeHealthInlineTextClass(string? status) =>
        Epic33UiDisplayHelpers.GetStatusCssClass(status ?? "");

    /// <summary>Integration lifecycle state from operator detail APIs.</summary>
    public static ManagementStatusSemantic MapIntegrationState(string? state)
    {
        if (string.Equals(state, "Active", StringComparison.OrdinalIgnoreCase))
            return ManagementStatusSemantic.Success;
        if (string.Equals(state, "Inactive", StringComparison.OrdinalIgnoreCase))
            return ManagementStatusSemantic.Warning;
        return ManagementStatusSemantic.Neutral;
    }

    public static string GetIntegrationStatePillClass(string? state) =>
        GetStatusPillClass(MapIntegrationState(state));

    /// <summary>
    /// Run lifecycle status from <see cref="Apperture.Management.Client.RunDetailDto.Status"/> (API string; presentation-only mapping).
    /// </summary>
    public static ManagementStatusSemantic MapRunLifecycleStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return ManagementStatusSemantic.Unknown;

        return status.Trim().ToLowerInvariant() switch
        {
            "completed" or "succeeded" or "success" => ManagementStatusSemantic.Success,
            "failed" or "faulted" or "error" => ManagementStatusSemantic.Error,
            "running" or "in_progress" or "inprogress" or "claimed" or "pending" or "queued" or "starting" =>
                ManagementStatusSemantic.Pending,
            "cancelled" or "canceled" => ManagementStatusSemantic.Warning,
            "parked" => ManagementStatusSemantic.Warning,
            _ => ManagementStatusSemantic.Neutral
        };
    }

    /// <summary>
    /// Run result outcome from <see cref="Apperture.Management.Client.RunResultDto.Outcome"/> (presentation-only; unknown tokens map to neutral).
    /// </summary>
    public static ManagementStatusSemantic MapRunOutcomeSemantic(string? outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome))
            return ManagementStatusSemantic.Unknown;

        var o = outcome.Trim().ToLowerInvariant();
        if (o.Contains("success", StringComparison.Ordinal) || o is "succeeded" or "completed")
            return ManagementStatusSemantic.Success;
        if (o.Contains("fail", StringComparison.Ordinal) || o.Contains("error", StringComparison.Ordinal) || o is "faulted")
            return ManagementStatusSemantic.Error;
        if (o.Contains("cancel", StringComparison.Ordinal))
            return ManagementStatusSemantic.Warning;
        return ManagementStatusSemantic.Neutral;
    }
}
