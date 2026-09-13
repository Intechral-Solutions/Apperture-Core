namespace Apperture.RuntimePlane.Engine.PlatformEvents;

/// <summary>
/// SWM-6 hotfix: maps start-request correlation onto platform-event columns without silent truncation.
/// </summary>
public static class PlatformEventScopeCorrelation
{
    /// <summary>Matches <c>PlatformEvents.CausationId</c> (<c>nvarchar(64)</c>).</summary>
    public const int CausationIdMaxLength = 64;

    /// <summary>Matches <c>WorkflowStartRequests.IdempotencyKey</c> / <c>OperationStartRequests.IdempotencyKey</c>.</summary>
    public const int StartRequestIdempotencyKeyMaxLength = 128;

    /// <summary>
    /// Causation for platform events is the immediate run id (bounded ≤ <see cref="CausationIdMaxLength"/>).
    /// Do not copy <c>StartRequest.IdempotencyKey</c> into CausationId: schedule fire keys
    /// (<c>schedule:{scheduleId}:{fireUtc:O}</c>) exceed 64 characters. Fire identity remains on the
    /// start-request idempotency column (≤128); schedule lineage uses CorrelationId / ScheduleId scope.
    /// </summary>
    public static string? CausationFromRunId(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return null;

        var value = runId.Trim();
        if (value.Length > CausationIdMaxLength)
        {
            throw new ArgumentException(
                $"RunId/CausationId length {value.Length} exceeds PlatformEvents.CausationId max {CausationIdMaxLength}.",
                nameof(runId));
        }

        return value;
    }
}
