namespace Apperture.Core.Application.PlatformEvents;

/// <summary>Canonical platform event type strings (v1 catalog).</summary>
public static class PlatformEventTypes
{
    public const string ScheduleExpectedRunMissing = "apperture.schedule.expected_run.missing";
    public const string ScheduleExpectedRunCleared = "apperture.schedule.expected_run.cleared";
    public const string ExecutionRunFailed = "apperture.execution.run.failed";
    public const string ExecutionRunCompleted = "apperture.execution.run.completed";
}
