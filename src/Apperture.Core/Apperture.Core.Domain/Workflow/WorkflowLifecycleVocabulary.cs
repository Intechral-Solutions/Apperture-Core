namespace Apperture.Core.Domain.Workflow;

/// <summary>
/// Shared workflow lifecycle vocabulary persisted in SQL and used by control plane, workers, and queries.
/// Lives in Core.Domain so persistence does not depend on RuntimePlane assemblies.
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

/// <summary>
/// External control request kinds the workflow runtime recipient model can accept.
/// </summary>
public static class WorkflowExternalControlKindNames
{
    public const string Park = "park";
    public const string Resume = "resume";
}

/// <summary>
/// Durable lifecycle state for queued workflow external control requests.
/// </summary>
public static class WorkflowExternalControlStatusNames
{
    public const string Queued = "queued";
    public const string Applied = "applied";
    public const string Rejected = "rejected";
}

/// <summary>
/// External control request kinds the runtime recipient model can accept.
/// </summary>
public static class RuntimeControlKindNames
{
    public const string Shutdown = "shutdown";
}

/// <summary>
/// Durable lifecycle state for queued runtime external control requests.
/// </summary>
public static class RuntimeControlStatusNames
{
    public const string Queued = "queued";
    public const string Applied = "applied";
    public const string Rejected = "rejected";
    public const string Failed = "failed";
}

/// <summary>
/// Persisted on operation runs (<c>OperationRuns.ExecutionOrigin</c>) to distinguish schedule-fired vs manual/procedure starts (Epic 64).
/// </summary>
public static class RunExecutionOriginNames
{
    public const string Schedule = "schedule";
    public const string Manual = "manual";
}
