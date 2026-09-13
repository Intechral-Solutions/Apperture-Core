namespace Apperture.Core.Application.Integration;

/// <summary>Epic 68: operator-facing slice of a persisted attention incident.</summary>
public sealed record AttentionIncidentProjection(
    string Id,
    string IntegrationId,
    string ConditionKey,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastObservedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    string? ResolutionReason,
    DateTimeOffset? FirstExpectedFireAtUtc,
    DateTimeOffset? LastExpectedFireAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    string? AcknowledgedBy,
    string? AcknowledgementNote,
    /// <summary>CTX-2: immutable ResourceContext snapshot taken when the incident opened.</summary>
    string? ResourceContextId = null);
