namespace Apperture.Core.Application.Services;

/// <summary>
/// Read model for audit timeline discoverability. Epic 32.
/// </summary>
public sealed record AuditTimelineEntry(
    string Id,
    DateTimeOffset Timestamp,
    string Actor,
    string Action,
    string Category,
    string Outcome,
    string Summary,
    string? Details,
    IReadOnlyList<AuditRelatedReference> RelatedReferences);

/// <summary>
/// Related entity reference for drill-down from an audit entry.
/// </summary>
public sealed record AuditRelatedReference(
    string Kind,
    string Id);

/// <summary>
/// Paginated audit timeline result.
/// </summary>
public sealed record AuditTimelinePage(
    IReadOnlyList<AuditTimelineEntry> Items,
    string? NextContinuationToken);

/// <summary>
/// Filter criteria for audit timeline queries.
/// </summary>
public sealed record AuditFilterCriteria(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? Actor = null,
    string? ActionPrefix = null,
    string? ModuleId = null,
    string? ScheduleId = null,
    string? RunId = null,
    string? Outcome = null,
    int PageSize = 50,
    string? ContinuationToken = null);
