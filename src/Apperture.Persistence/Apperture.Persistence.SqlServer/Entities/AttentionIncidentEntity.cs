namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Epic 68: persisted health/attention incident for a condition (e.g. expected_run_missing).
/// </summary>
public sealed class AttentionIncidentEntity
{
    public string Id { get; set; } = "";
    public string IntegrationId { get; set; } = "";

    /// <summary>
    /// CTX-2: immutable ResourceContext snapshot taken from the Integration's ResourceContext at
    /// the moment this incident opened. Preserved for the incident's lifetime; never rewritten by
    /// later observation, reminder, escalation or resolution. Null for pre-CTX-2 rows.
    /// </summary>
    public string? ResourceContextId { get; set; }
    public string ConditionKey { get; set; } = "";
    /// <summary>Active or Resolved.</summary>
    public string Status { get; set; } = "";
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset LastObservedAtUtc { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public string? ResolutionReason { get; set; }
    public DateTimeOffset? FirstExpectedFireAtUtc { get; set; }
    public DateTimeOffset? LastExpectedFireAtUtc { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
    public string? AcknowledgedBy { get; set; }
    public string? AcknowledgementNote { get; set; }
}
