namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// Shared workflow lifecycle vocabulary used by control-plane services, persistence, and workers.
/// </summary>
public static class WorkflowLifecycleStatusNames
{
    public const string Requested = "requested";
    public const string Running = "running";
    public const string Parked = "parked";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// Durable lifecycle event names recorded for run, control-request, and step transitions.
/// </summary>
public static class WorkflowLifecycleEventNames
{
    public const string Dispatched = "workflow.dispatched";
    public const string Started = "workflow.started";
    public const string Completed = "workflow.completed";
    public const string Failed = "workflow.failed";
    public const string CancelRequested = "workflow.cancel_requested";
    public const string Cancelled = "workflow.cancelled";
    public const string ParkRequested = "workflow.park_requested";
    public const string Parked = "workflow.parked";
    public const string ResumeRequested = "workflow.resume_requested";
    public const string Resumed = "workflow.resumed";
    public const string RetryRequested = "workflow.retry_requested";
    public const string StepStarted = "workflow.step_started";
    public const string StepCompleted = "workflow.step_completed";
    public const string StepFailed = "workflow.step_failed";
}
