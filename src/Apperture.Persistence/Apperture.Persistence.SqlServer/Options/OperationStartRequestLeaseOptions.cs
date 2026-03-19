namespace Apperture.Persistence.SqlServer.Options;

/// <summary>
/// Lease settings for OperationStartRequests processing.
/// </summary>
public sealed class OperationStartRequestLeaseOptions
{
    /// <summary>
    /// Duration after which a running (claimed) request is considered stale and can be reclaimed by another worker.
    /// The active lease owner must renew (heartbeat) by updating ClaimedAt while executing.
    /// </summary>
    public TimeSpan LeaseTimeout { get; init; } = TimeSpan.FromMinutes(10);
}

