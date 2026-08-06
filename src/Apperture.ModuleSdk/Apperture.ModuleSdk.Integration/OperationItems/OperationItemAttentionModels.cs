namespace Apperture.Core.Application.OperationItems;

/// <summary>JOB-1 — operator acknowledgement dispositions for failed operation items.</summary>
public static class OperationItemAttentionDispositions
{
    public const string ResolvedExternally = "ResolvedExternally";
    public const string NoActionRequired = "NoActionRequired";
    public const string WillNotRetry = "WillNotRetry";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ResolvedExternally,
        NoActionRequired,
        WillNotRetry,
    };

    public static bool IsKnown(string? disposition) =>
        !string.IsNullOrWhiteSpace(disposition) && All.Contains(disposition.Trim());

    public static string? Normalize(string? disposition)
    {
        if (string.IsNullOrWhiteSpace(disposition))
            return null;
        var trimmed = disposition.Trim();
        foreach (var known in All)
        {
            if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
                return known;
        }

        // kebab-case API aliases
        return trimmed.ToLowerInvariant() switch
        {
            "resolved-externally" or "resolvedexternally" => ResolvedExternally,
            "no-action-required" or "noactionrequired" => NoActionRequired,
            "will-not-retry" or "willnotretry" => WillNotRetry,
            _ => null,
        };
    }

    public static string ToOperatorLabel(string disposition) =>
        disposition switch
        {
            ResolvedExternally => "Resolved externally",
            NoActionRequired => "No action required",
            WillNotRetry => "Will not retry",
            _ => disposition,
        };
}

/// <summary>JOB-1 — size bounds for acknowledgement fields.</summary>
public static class OperationItemAttentionBounds
{
    public const int MaxBulkItemCount = 100;
    public const int ReasonMaxLength = 512;
    public const int DispositionMaxLength = 64;
    public const int ActorIdMaxLength = 128;
    public const int IdMaxLength = 64;
}

/// <summary>JOB-1 — durable acknowledgement of failed-item attention (not an execution outcome).</summary>
public sealed record OperationItemAttentionAcknowledgement(
    string AcknowledgementId,
    string OperationItemId,
    string RunId,
    string OperationGroupId,
    string Disposition,
    string? Reason,
    DateTimeOffset AcknowledgedAtUtc,
    string AcknowledgedByUserId,
    DateTimeOffset? ReopenedAtUtc,
    string? ReopenedByUserId,
    DateTimeOffset CreatedAtUtc)
{
    public bool IsActive => ReopenedAtUtc is null;
}

public sealed record CreateOperationItemAttentionAcknowledgementRequest(
    string OperationItemId,
    string RunId,
    string OperationGroupId,
    string Disposition,
    string? Reason,
    string AcknowledgedByUserId,
    DateTimeOffset? AcknowledgedAtUtc = null);

/// <summary>
/// JOB-1 attention helpers. Execution outcome stays Failed; acknowledgement clears attention only.
/// </summary>
public static class OperationItemAttentionRules
{
    /// <summary>
    /// Needs attention when the item failed, has no active acknowledgement, and no successful child retry.
    /// Active (in-flight) retries leave attention until they succeed or the operator acknowledges.
    /// </summary>
    public static bool NeedsAttention(
        string status,
        bool hasActiveAcknowledgement,
        bool hasSucceededRetryAttempt) =>
        string.Equals(status, OperationItemStatuses.Failed, StringComparison.Ordinal)
        && !hasActiveAcknowledgement
        && !hasSucceededRetryAttempt;

    public static bool IsActiveRetryStatus(string? status) =>
        string.Equals(status, OperationItemRetryAttemptStatuses.Requested, StringComparison.Ordinal)
        || string.Equals(status, OperationItemRetryAttemptStatuses.Dispatched, StringComparison.Ordinal);

    public static bool IsSucceededRetryStatus(string? status) =>
        string.Equals(status, OperationItemRetryAttemptStatuses.Succeeded, StringComparison.Ordinal);
}
