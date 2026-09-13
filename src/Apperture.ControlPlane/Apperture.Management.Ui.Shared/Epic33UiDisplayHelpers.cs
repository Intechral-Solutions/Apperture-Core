using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Epic 33: Testable display logic for blocked work, schedule action semantics,
/// control center health, and audit cross-navigation. Single source of truth for UI truthfulness tests.
/// </summary>
public static class Epic33UiDisplayHelpers
{
    // --- Blocked Work ---

    /// <summary>Retry-eligible display for blocked work table: "Inspection only", "Yes", or reason.</summary>
    public static string GetBlockedWorkRetryEligibleDisplay(BlockedWorkRemediationEntryDto item)
    {
        if (string.Equals(item.DefinitionKind, "operation", StringComparison.OrdinalIgnoreCase))
            return "Inspection only";
        if (item.RetryEligibleNow)
            return "Yes";
        return item.RetryEligibleReason ?? "No";
    }

    /// <summary>Action cell: shows Retry button for eligible workflow, View run for operation, "-" otherwise.</summary>
    public static BlockedWorkActionKind GetBlockedWorkActionKind(BlockedWorkRemediationEntryDto item)
    {
        if (string.Equals(item.DefinitionKind, "workflow", StringComparison.OrdinalIgnoreCase) && item.RetryEligibleNow)
            return BlockedWorkActionKind.Retry;
        if (string.Equals(item.DefinitionKind, "operation", StringComparison.OrdinalIgnoreCase))
            return BlockedWorkActionKind.ViewRun;
        return BlockedWorkActionKind.None;
    }

    public enum BlockedWorkActionKind { Retry, ViewRun, None }

    /// <summary>True when the UI shows a Retry button (workflow + eligible).</summary>
    public static bool ShowsRetryButton(BlockedWorkRemediationEntryDto item) =>
        GetBlockedWorkActionKind(item) == BlockedWorkActionKind.Retry;

    /// <summary>True when the UI shows inspection-only messaging (operation items).</summary>
    public static bool ShowsInspectionOnly(BlockedWorkRemediationEntryDto item) =>
        string.Equals(item.DefinitionKind, "operation", StringComparison.OrdinalIgnoreCase);

    // --- Schedule action semantics (global re-evaluation, NOT schedule-specific) ---

    public const string ScheduleActionButtonLabel = "Trigger scheduler re-evaluation";
    public const string ScheduleActionButtonTitle = "Trigger global scheduler re-evaluation. Not schedule-specific.";
    public const string ScheduleActionExplanatoryText = "Trigger scheduler re-evaluation runs a global cycle for all schedules due at the current time. It is not a schedule-specific force-fire.";

    /// <summary>Verifies schedule action semantics are global, not schedule-specific.</summary>
    public static bool ScheduleActionIsGlobalSemantics =>
        ScheduleActionButtonLabel.Contains("re-evaluation", StringComparison.Ordinal) &&
        ScheduleActionExplanatoryText.Contains("global", StringComparison.OrdinalIgnoreCase) &&
        ScheduleActionExplanatoryText.Contains("not a schedule-specific", StringComparison.OrdinalIgnoreCase) &&
        !ScheduleActionExplanatoryText.Contains("run now", StringComparison.OrdinalIgnoreCase) &&
        !ScheduleActionExplanatoryText.Contains("fire this schedule", StringComparison.OrdinalIgnoreCase);

    // --- Control center platform health ---

    public record ControlCenterHealthCounts(int DegradedModuleCount, int BlockedScheduleCount, int BlockedWorkCount);

    public static ControlCenterHealthCounts GetPlatformHealthCounts(RuntimeHealthSnapshotDto health)
    {
        var degradedCount = health.ModuleDiagnostics.Count(m =>
            !m.EffectivelyExecutable || m.ExecutionDegradedReason is not null);
        var blockedSchedules = health.ScheduleDiagnostics.Count(s =>
            s.ScheduleHealthStatus == "Blocked" || s.ScheduleHealthStatus == "Degraded");
        var blockedWorkCount = health.BlockedDeferredWork.Count;
        return new ControlCenterHealthCounts(degradedCount, blockedSchedules, blockedWorkCount);
    }

    public static string GetStatusCssClass(string status) =>
        status == "Healthy" ? "text-success" : status == "Degraded" ? "text-warning" : "text-error";

    // --- Audit cross-navigation (links from module/schedule/run context) ---

    public static string GetAuditRefLink(AuditRelatedReferenceDto r) => r.Kind switch
    {
        "run" => $"/runs/{r.Id}",
        "schedule" => $"/schedules/{r.Id}",
        "module" => string.IsNullOrEmpty(r.Id) ? "/modules" : $"/modules/{r.Id}",
        _ => "#"
    };

    public static string GetAuditLinkForModule(string moduleId) =>
        $"/control/audit?moduleId={Uri.EscapeDataString(moduleId)}";

    public static string GetAuditLinkForSchedule(string scheduleId) =>
        $"/control/audit?scheduleId={Uri.EscapeDataString(scheduleId)}";

    public static string GetAuditLinkForRun(string runId) =>
        $"/control/audit?runId={Uri.EscapeDataString(runId)}";

    public const string RecoveryAuditActionPrefix = "recovery.";
    public static string GetRecoveryAuditLink() =>
        $"/control/audit?actionPrefix={Uri.EscapeDataString(RecoveryAuditActionPrefix)}";

    public const string GovernanceAuditActionPrefix = "governance.";
    public static string GetGovernanceAuditLink() =>
        $"/control/audit?actionPrefix={Uri.EscapeDataString(GovernanceAuditActionPrefix)}";

    // --- Run forensics (Epic 35) ---

    public static string FormatRunOriginKind(string? originKind) =>
        originKind?.ToLowerInvariant() switch
        {
            "retry" => "Retry (new run)",
            "scheduled" => "Scheduled dispatch",
            "manual" => "Manual / API start",
            "unknown" or null => "Unknown",
            _ => originKind ?? "Unknown"
        };

    /// <summary>Shown when origin is inferred from idempotency convention, not a stored FK.</summary>
    public static string RunScheduleInferenceDisclaimer =>
        "Schedule link is inferred from the platform idempotency key convention (schedule:{id}:{time}). It is not a separate persisted association.";

    /// <summary>Label for module link when resolved from workflow/operation definition. Used so UI does not overclaim inferred causality.</summary>
    public static string RunForensicModuleLabel => "Module (from definition)";

    /// <summary>Builds relative URL for investigation hint kinds from <see cref="Apperture.Management.Client.RunInvestigationHintDto"/>.</summary>
    public static string GetRunInvestigationHintHref(string kind, string? targetId) =>
        kind.ToLowerInvariant() switch
        {
            "audit" when !string.IsNullOrEmpty(targetId) => GetAuditLinkForRun(targetId),
            "module" when !string.IsNullOrEmpty(targetId) => $"/modules/{Uri.EscapeDataString(targetId)}",
            "schedule" when !string.IsNullOrEmpty(targetId) => $"/schedules/{Uri.EscapeDataString(targetId)}",
            "blocked_work" => RunStatusFacets.BlockedRunsHref,
            "runtime_health" => "/control/center",
            _ => "#"
        };

    public static string GetRunsListCorrelationFilterHref(string correlationKey) =>
        $"/runs?correlationKey={Uri.EscapeDataString(correlationKey)}";

    /// <summary>Runs list filtered by integration id (Epic 94/95).</summary>
    public static string GetRunsListIntegrationFilterHref(string integrationId) =>
        $"/runs?integrationId={Uri.EscapeDataString(integrationId)}";

    /// <summary>Runs list filtered by module id on execution definition (Epic 95).</summary>
    public static string GetRunsListModuleFilterHref(string moduleId) =>
        $"/runs?moduleId={Uri.EscapeDataString(moduleId)}";

    /// <summary>
    /// Builds a <c>/runs</c> URL preserving every supplied sibling filter. Used so changing one
    /// filter (e.g. the status facet dropdown) never silently discards the others — a status
    /// change alters only <paramref name="status"/>. Omit/blank a parameter to clear it.
    /// </summary>
    public static string BuildRunsListUrl(
        string? correlationKey = null,
        string? status = null,
        string? createdFrom = null,
        string? integrationId = null,
        string? moduleId = null)
    {
        var parts = new List<string>(5);

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add($"{key}={Uri.EscapeDataString(value)}");
        }

        Add("correlationKey", correlationKey);
        Add("status", status);
        Add("createdFrom", createdFrom);
        Add("integrationId", integrationId);
        Add("moduleId", moduleId);

        return parts.Count == 0 ? "/runs" : $"/runs?{string.Join("&", parts)}";
    }
}
