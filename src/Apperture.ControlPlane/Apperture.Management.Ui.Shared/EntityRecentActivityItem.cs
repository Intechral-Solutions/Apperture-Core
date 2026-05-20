namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Epic 94: Single row in an entity-local recent activity panel (merged from existing API sources).
/// </summary>
public sealed record EntityRecentActivityItem(
    DateTimeOffset TimestampUtc,
    string SourceKind,
    string TypeLabel,
    string Summary,
    string? Actor,
    string? StatusOrOutcome,
    string? DrillInHref,
    string DrillInLabel);
