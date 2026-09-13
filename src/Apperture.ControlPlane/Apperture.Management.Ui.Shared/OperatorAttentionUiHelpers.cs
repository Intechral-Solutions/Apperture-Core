namespace Apperture.Management.Ui.Shared;

using Apperture.Management.Client;

/// <summary>
/// Epic S4: operator dashboard and control-center display helpers (testable, UI-facing).
/// </summary>
public static class OperatorAttentionUiHelpers
{
    public static int DashboardAttentionSortRank(string? attention) =>
        attention?.Trim().ToLowerInvariant() switch
        {
            "critical" => 0,
            "warning" => 1,
            "info" => 2,
            _ => 3
        };

    public static string? GetControlCenterAttentionHref(ControlAttentionItemDto item)
    {
        if (!string.IsNullOrWhiteSpace(item.RelatedActivityId))
        {
            if (string.Equals(item.AttentionKind, "executionFailed", StringComparison.Ordinal)
                || string.Equals(item.AttentionKind, "workflowControlRejected", StringComparison.Ordinal))
                return $"/runs/{Uri.EscapeDataString(item.RelatedActivityId)}";

            if (string.Equals(item.AttentionKind, "runtimeShutdownQueuedStuck", StringComparison.Ordinal))
            {
                return $"/runtimes/{Uri.EscapeDataString(item.TargetId)}/controls/shutdown/requests/{Uri.EscapeDataString(item.RelatedActivityId)}";
            }
        }

        if (string.Equals(item.TargetKind, "schedule", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(item.TargetId))
            return $"/schedules/{Uri.EscapeDataString(item.TargetId)}";

        if (string.Equals(item.TargetKind, "workflow", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(item.TargetId))
            return $"/runs?workflowDefinitionId={Uri.EscapeDataString(item.TargetId)}";

        return null;
    }

    public static bool IsProblematicActivity(ControlActivitySummaryDto activity)
    {
        if (string.Equals(activity.Outcome, "failed", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(activity.CurrentNormalizedStatus, "failed", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatAttentionSummaryForDisplay(string summary) =>
        summary.Replace("runId=", "run ", StringComparison.OrdinalIgnoreCase);
}
