namespace Apperture.ControlPlane.Security;

/// <summary>
/// Stable origin kind strings for <see cref="RunForensicContextDto.OriginKind"/>. Epic 35.
/// </summary>
public static class RunOriginKindNames
{
    public const string Unknown = "unknown";
    public const string Manual = "manual";
    public const string Scheduled = "scheduled";
    public const string Retry = "retry";
}

/// <summary>
/// Classifies run initiation from durable start-request and run fields only. Epic 35.
/// </summary>
public static class RunOriginClassifier
{
    /// <summary>
    /// Parses platform schedule dispatch idempotency: <c>schedule:{scheduleId}:{fireTimeUtc:O}</c> from <see cref="ScheduleDispatchRunner"/>.
    /// </summary>
    public static (string ScheduleId, DateTimeOffset FireUtc)? TryParseScheduleDispatchIdempotency(string? idempotencyKey)
    {
        if (string.IsNullOrEmpty(idempotencyKey))
            return null;
        const string prefix = "schedule:";
        if (!idempotencyKey.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        var rest = idempotencyKey[prefix.Length..];
        var idx = rest.IndexOf(':');
        if (idx <= 0 || idx >= rest.Length - 1)
            return null;
        var scheduleId = rest[..idx];
        if (string.IsNullOrWhiteSpace(scheduleId))
            return null;
        var iso = rest[(idx + 1)..];
        if (!DateTimeOffset.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var fire))
            return null;
        return (scheduleId, fire);
    }

    /// <summary>
    /// First match: Retry (RetryOfRunId), Scheduled (idempotency convention), Manual (has start row), Unknown.
    /// </summary>
    public static (string OriginKind, string OriginDetail, string? ScheduleId, DateTimeOffset? ScheduleFireUtc) Classify(
        string? retryOfRunId,
        string? startIdempotencyKey,
        string? startCorrelationId,
        bool hasStartRequest)
    {
        if (!string.IsNullOrEmpty(retryOfRunId))
        {
            return (
                RunOriginKindNames.Retry,
                "Retry-as-new-run: RetryOfRunId links to the original run. API retry and recovery retry use the same field; not distinguished.",
                null,
                null);
        }

        var scheduleParsed = TryParseScheduleDispatchIdempotency(startIdempotencyKey);
        if (scheduleParsed is not null)
        {
            var (sid, fire) = scheduleParsed.Value;
            var expectedCorr = $"schedule:{sid}";
            var corrOk = string.IsNullOrEmpty(startCorrelationId)
                         || string.Equals(startCorrelationId, expectedCorr, StringComparison.Ordinal);
            var detail = corrOk
                ? "Inferred scheduled dispatch from start idempotency key (platform schedule convention)."
                : "Inferred scheduled dispatch from idempotency key; start correlation does not match expected schedule:{scheduleId} pattern.";
            return (RunOriginKindNames.Scheduled, detail, sid, fire);
        }

        if (hasStartRequest)
        {
            return (
                RunOriginKindNames.Manual,
                "Started via persisted control-plane start request (not schedule idempotency pattern, not a retry-as-new-run).",
                null,
                null);
        }

        return (
            RunOriginKindNames.Unknown,
            "No persisted start request or insufficient metadata to classify origin.",
            null,
            null);
    }
}
