namespace Apperture.Management.Client;

/// <summary>Epic 50 / SWM-3: Create schedule for Integration. Null/blank TimeZone → platform Scheduling.DefaultTimeZoneId.</summary>
public sealed record CreateIntegrationScheduleRequest(
    string Name,
    string RecurrenceExpression,
    string? TimeZone = null,
    bool Enabled = true,
    int? TargetJobType = 0,
    string? TargetJobConfiguration = null,
    string? ExecutionPolicyRef = null,
    string? RuntimePlaneId = "local");

/// <summary>Epic 38 / SWM-3: Create schedule request. Null/blank TimeZone → platform Scheduling.DefaultTimeZoneId.</summary>
public sealed record CreateScheduleRequest(
    string Name,
    string RecurrenceExpression,
    string? TimeZone = null,
    bool Enabled = true,
    int? TargetJobType = 0,
    string? TargetJobConfiguration = null,
    string? ExecutionPolicyRef = null,
    string? RuntimePlaneId = "local",
    string? IntegrationId = null);

/// <summary>Epic 38: Update schedule request. Shape matches API UpdateScheduleRequestBody.</summary>
public sealed record UpdateScheduleRequest(
    string? Name = null,
    string? RecurrenceExpression = null,
    string? TimeZone = null,
    bool? Enabled = null);

/// <summary>Epic 38: Thrown when schedule create/update returns 400 with validation errors.</summary>
public sealed class ScheduleValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ScheduleValidationException(IReadOnlyList<string> errors)
        : base($"Schedule validation failed: {string.Join("; ", errors)}")
    {
        Errors = errors;
    }
}

public sealed record ScheduleCapabilitiesDto(bool CanManage);

/// <summary>SWM-10: stable schedule status reason (code + operator message).</summary>
public sealed record ScheduleStatusReasonDto(string Code, string Message);

public sealed record ScheduleDto(
    string Id,
    string Name,
    string RecurrenceExpression,
    string TimeZone,
    bool Enabled,
    int TargetJobType,
    string? TargetJobConfiguration,
    int Version,
    string? IntegrationId = null,
    DateTimeOffset? NextFireUtc = null,
    string? ScheduleTimeZoneId = null,
    string? HealthStatus = null,
    IReadOnlyList<ScheduleStatusReasonDto>? StatusReasons = null);

/// <summary>
/// Schedule with execution state for operator visibility. Epic 28, Epic 36 (claim visibility).
/// Claim fields: populated only when a schedule run is <strong>in progress</strong> for this plane's assignment; absent once completed.
/// SWM-10: additive next-fire / readiness projection fields (compute-only, not persisted).
/// </summary>
public sealed record ScheduleDetailDto(
    string Id,
    string Name,
    string RecurrenceExpression,
    string TimeZone,
    bool Enabled,
    int TargetJobType,
    string? TargetJobConfiguration,
    int Version,
    DateTimeOffset? LastAttemptedUtc,
    string? LastOutcome,
    string? LastOutcomeReason,
    DateTimeOffset? LastSuccessfulDispatchUtc,
    string? ClaimedByHostId = null,
    DateTimeOffset? ClaimedAtUtc = null,
    bool? IsClaimStale = null,
    string? IntegrationId = null,
    string? RuntimePlaneId = null,
    DateTimeOffset? NextFireUtc = null,
    string? ScheduleTimeZoneId = null,
    string? HealthStatus = null,
    IReadOnlyList<ScheduleStatusReasonDto>? StatusReasons = null);
