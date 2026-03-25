namespace Apperture.Core.Application.Integration;

/// <summary>
/// Query for fleet list filtering and sort. Epic 61.
/// </summary>
public sealed record IntegrationFleetQuery(
    string? State = null,
    bool? Runnable = null,
    bool? HasBlocking = null,
    bool? HasAdvisory = null,
    bool? VerificationConcern = null,
    string? Verification = null,
    IReadOnlyList<string>? AttentionTokens = null,
    string? Sort = null);

/// <summary>
/// Evidence-backed fleet attention flags. No scoring; derived from operational state and readiness issues only.
/// </summary>
public sealed record IntegrationFleetAttentionFlags(
    bool IsInactiveOperational,
    bool IsRetired,
    /// <summary>Blocking readiness issues excluding <see cref="IntegrationReadinessIssueCodes.IntegrationNotActive"/> (configuration/safety vs paused).</summary>
    bool HasConfigBlockingReadiness,
    /// <summary>True when <see cref="IntegrationReadinessProjection.IsRunnable"/> and there is at least one advisory issue.</summary>
    bool HasAdvisoryOnly,
    bool HasVerificationFailed,
    bool HasVerificationUnverified,
    bool HasVerificationUnavailable);

/// <summary>
/// One row of the integration fleet overview. Epic 61.
/// </summary>
public sealed record IntegrationFleetRowProjection(
    string Id,
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    string OperationalState,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IntegrationReadinessProjection Readiness,
    IntegrationFleetAttentionFlags Attention,
    IReadOnlyList<string> AttentionTokens,
    IReadOnlyList<string> TopIssueCodes);

/// <summary>
/// Computes fleet attention from persisted-projection semantics (readiness issues + operational state).
/// </summary>
public static class IntegrationFleetAttention
{
    /// <summary>
    /// Stable tokens returned in <see cref="IntegrationFleetRowProjection.AttentionTokens"/> and accepted by fleet query <c>attention</c>.
    /// </summary>
    public static class Tokens
    {
        public const string Inactive = "inactive";
        public const string Retired = "retired";
        public const string Blocking = "blocking";
        public const string AdvisoryOnly = "advisory_only";
        public const string VerificationFailed = "verification_failed";
        public const string VerificationUnverified = "verification_unverified";
        public const string VerificationUnavailable = "verification_unavailable";
    }

    public static IntegrationFleetAttentionFlags Compute(
        string operationalState,
        IntegrationReadinessProjection readiness)
    {
        var inactive = string.Equals(operationalState, "Inactive", StringComparison.OrdinalIgnoreCase);
        var retired = string.Equals(operationalState, "Retired", StringComparison.OrdinalIgnoreCase);

        var hasVerificationFailed = readiness.Issues.Any(i =>
            string.Equals(i.Code, IntegrationReadinessIssueCodes.ConnectionVerificationFailed, StringComparison.Ordinal));
        var hasVerificationUnverified = readiness.Issues.Any(i =>
            string.Equals(i.Code, IntegrationReadinessIssueCodes.ConnectionVerificationUnverified, StringComparison.Ordinal));
        var hasVerificationUnavailable = readiness.Issues.Any(i =>
            string.Equals(i.Code, IntegrationReadinessIssueCodes.ConnectionVerificationUnavailable, StringComparison.Ordinal));

        var hasConfigBlocking = readiness.Issues.Any(i =>
            string.Equals(i.Severity, IntegrationReadinessIssueSeverity.Blocking.ToString(), StringComparison.Ordinal) &&
            !string.Equals(i.Code, IntegrationReadinessIssueCodes.IntegrationNotActive, StringComparison.Ordinal));

        var advisoryOnly = readiness.IsRunnable && readiness.AdvisoryIssueCount > 0;

        return new IntegrationFleetAttentionFlags(
            inactive,
            retired,
            hasConfigBlocking,
            advisoryOnly,
            hasVerificationFailed,
            hasVerificationUnverified,
            hasVerificationUnavailable);
    }

    public static IReadOnlyList<string> ToAttentionTokens(IntegrationFleetAttentionFlags f)
    {
        var list = new List<string>(8);
        if (f.IsInactiveOperational)
            list.Add(Tokens.Inactive);
        if (f.IsRetired)
            list.Add(Tokens.Retired);
        if (f.HasConfigBlockingReadiness)
            list.Add(Tokens.Blocking);
        if (f.HasAdvisoryOnly)
            list.Add(Tokens.AdvisoryOnly);
        if (f.HasVerificationFailed)
            list.Add(Tokens.VerificationFailed);
        if (f.HasVerificationUnverified)
            list.Add(Tokens.VerificationUnverified);
        if (f.HasVerificationUnavailable)
            list.Add(Tokens.VerificationUnavailable);
        return list;
    }

    /// <summary>
    /// Short verification summary for table columns: evidence from issue codes, not a separate health layer.
    /// </summary>
    public static string VerificationSummary(IntegrationFleetAttentionFlags f)
    {
        if (f.HasVerificationFailed)
            return "Failed";
        if (f.HasVerificationUnavailable)
            return "Unavailable";
        if (f.HasVerificationUnverified)
            return "Unverified";
        return "OK";
    }

    public static IReadOnlyList<string> TopIssueCodes(IntegrationReadinessProjection readiness, int max = 5)
    {
        var blocking = readiness.Issues
            .Where(i => string.Equals(i.Severity, IntegrationReadinessIssueSeverity.Blocking.ToString(), StringComparison.Ordinal))
            .Select(i => i.Code)
            .Distinct(StringComparer.Ordinal);
        var advisory = readiness.Issues
            .Where(i => string.Equals(i.Severity, IntegrationReadinessIssueSeverity.Advisory.ToString(), StringComparison.Ordinal))
            .Select(i => i.Code)
            .Distinct(StringComparer.Ordinal);
        return blocking.Concat(advisory).Take(max).ToList();
    }
}
