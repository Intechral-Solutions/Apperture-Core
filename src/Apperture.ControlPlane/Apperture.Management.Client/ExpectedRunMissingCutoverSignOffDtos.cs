namespace Apperture.Management.Client;

public sealed record ExpectedRunMissingCutoverSignOffDto(
    string Id,
    string IntegrationId,
    string NotificationDefinitionId,
    string Status,
    DateTimeOffset ReviewedAtUtc,
    string ReviewedByActorId,
    string? ReviewedByDisplayName,
    bool ParityAcceptableAtReview,
    IReadOnlyList<string> ConfigurationWarningCodes,
    IReadOnlyList<string> BlockingFindings,
    IReadOnlyList<string> NonBlockingFindings,
    string? OperatorNote,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateExpectedRunMissingCutoverSignOffRequestDto(
    string Status,
    string? OperatorNote);

public sealed record RevokeExpectedRunMissingCutoverSignOffRequestDto(string? OperatorNote);

public sealed record ExpectedRunMissingCutoverValidationDto(
    string IntegrationId,
    IReadOnlyList<string> OperatingModes,
    IReadOnlyList<string> OperatingModeLabels,
    bool HasReadyForCutoverSignOff,
    bool SignOffIsRevoked,
    bool SignOffIsStale,
    string? LatestSignOffStatus,
    DateTimeOffset? LatestSignOffReviewedAtUtc,
    bool ParityCurrentlyAcceptable,
    bool HasDangerousConfigurationWarnings,
    IReadOnlyList<string> ConfigurationWarningCodes,
    int RecentFailedOutboxCount,
    int RecentSucceededOutboxCount,
    bool HasUnresolvedFailedOutbox,
    bool HasRecipientParityGap,
    int RecipientsMissingInPlatform,
    IReadOnlyList<string> ReadinessBlockers,
    DateTimeOffset GeneratedAtUtc);
