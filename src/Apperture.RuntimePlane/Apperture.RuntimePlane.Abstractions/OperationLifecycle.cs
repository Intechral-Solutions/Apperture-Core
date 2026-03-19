namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// Durable lifecycle event names for operation executions.
/// </summary>
public static class OperationLifecycleEventNames
{
    public const string Dispatched = "operation.dispatched";
    public const string Started = "operation.started";
    public const string Completed = "operation.completed";
    public const string Failed = "operation.failed";

    public const string CancelRequested = "operation.cancel_requested";
    public const string Cancelled = "operation.cancelled";
}

