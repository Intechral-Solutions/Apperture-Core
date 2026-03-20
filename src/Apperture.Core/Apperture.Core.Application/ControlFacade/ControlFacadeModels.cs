using System.Text.RegularExpressions;

namespace Apperture.Core.Application.ControlFacade;

/// <summary>
/// Unified operator control facade: small, cohesive shells over multiple domain slices.
/// DTOs in this namespace are shared between control-plane services and controllers.
/// </summary>
public static class ControlFacade
{
    public const string TargetKindOperation = "operation";
    public const string TargetKindWorkflow = "workflow";
    public const string TargetKindRuntimeInstance = "runtimeInstance";
    public const string TargetKindSchedule = "schedule";

    public const string ActionKindOperationStart = "operation.start";
    public const string ActionKindWorkflowStart = "workflow.start";
    public const string ActionKindRuntimeShutdownRequest = "runtime.control.shutdown.request";

    // Run/activity actions (per-activity item, not per-target).
    public const string ActionKindCancel = "execution.cancel";
    public const string ActionKindRetry = "execution.retry";
    public const string ActionKindPark = "execution.park";
    public const string ActionKindResume = "execution.resume";

    public const string ActionAvailabilityAvailable = "available";
    public const string ActionAvailabilityUnavailable = "unavailable";
    public const string ActionAvailabilityAlreadyQueued = "alreadyQueued";
    public const string ActionAvailabilityNotEligible = "notEligible";

    public const string ReasonPermissionMissing = "permission_missing";
    public const string ReasonCapabilityMissing = "capability_missing";
    public const string ReasonStateNotEligible = "state_not_eligible";
    public const string ReasonAlreadyQueued = "already_queued";
    public const string ReasonUnknown = "unknown";

    // Small normalized status vocabulary suitable for top-level monitoring.
    public const string NormalizedRequested = "requested";
    public const string NormalizedRunning = "running";
    public const string NormalizedParked = "parked";
    public const string NormalizedSucceeded = "succeeded";
    public const string NormalizedFailed = "failed";
    public const string NormalizedCancelled = "cancelled";

    public const string ActivityKindExecutionRun = "executionRun";
    public const string ActivityKindRuntimeControlRequest = "runtimeControlRequest";

    public const string ActivitySourceDirect = "direct";
    public const string ActivitySourceScheduled = "scheduled";

    /// <summary>
    /// Parses a schedule id from a correlation key using a safe, explicit convention.
    /// Convention: a correlation key containing a token like <c>schedule:{scheduleId}</c>.
    /// </summary>
    public static bool TryParseScheduleId(string? correlationKey, out string scheduleId)
    {
        scheduleId = "";
        if (string.IsNullOrWhiteSpace(correlationKey))
            return false;

        // Protective parsing: require the exact "schedule:" token, but allow common correlation-key
        // separators/punctuation before it. Prevents false positives like "myschedule:..." by
        // ensuring the preceding character is not alphanumeric/underscore.
        var match = Regex.Match(
            correlationKey,
            @"(?<![A-Za-z0-9_])schedule:([A-Za-z0-9_.-]+)",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        var value = match.Groups[1].Value;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        scheduleId = value;
        return true;
    }

    public static string NormalizeRunStatus(string rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
            return NormalizedRequested;

        return rawStatus.ToLowerInvariant() switch
        {
            "requested" => NormalizedRequested,
            "running" => NormalizedRunning,
            "parked" => NormalizedParked,
            "completed" => NormalizedSucceeded,
            "succeeded" => NormalizedSucceeded,
            "failed" => NormalizedFailed,
            "cancelled" => NormalizedCancelled,
            "canceled" => NormalizedCancelled,
            _ => NormalizedRequested
        };
    }

    public static (string Outcome, string Summary) NormalizeRunOutcome(string rawStatus, string executionKind)
    {
        _ = executionKind;

        var normalized = NormalizeRunStatus(rawStatus);
        return normalized switch
        {
            NormalizedSucceeded => ("succeeded", "Execution completed successfully."),
            NormalizedCancelled => ("cancelled", "Execution was cancelled."),
            NormalizedFailed => ("failed", "Execution failed."),
            _ => ("in_progress", "Execution is still in progress.")
        };
    }
}

public sealed record ControlActionAvailabilityDto(
    string ActionKind,
    string AvailabilityState,
    string? ReasonCode);

public sealed record ControlTargetSummaryDto(
    string TargetKind,
    string TargetId,
    string DisplayName,
    string? GroupKey,
    string? DetailLink,
    IReadOnlyList<ControlActionAvailabilityDto> AvailableActions);

public sealed record ControlActivitySummaryDto(
    string ActivityKind,
    string ActivityId,
    string TargetKind,
    string TargetId,
    string RequestedAtIso,
    string? CompletedAtIso,
    string CurrentNormalizedStatus,
    string Outcome,
    string OutcomeSummary,
    string ActivitySource,
    string DetailLink,
    IReadOnlyList<ControlActionAvailabilityDto> AvailableActions);

public sealed record ControlAttentionItemDto(
    string AttentionKind,
    string Severity,
    string TargetKind,
    string TargetId,
    string Summary,
    string CreatedAtIso,
    string? RelatedActivityId);

public sealed record ControlCenterDto(
    IReadOnlyList<ControlTargetSummaryDto> Targets,
    IReadOnlyList<ControlActivitySummaryDto> RecentActivity,
    IReadOnlyList<ControlAttentionItemDto> AttentionItems);

