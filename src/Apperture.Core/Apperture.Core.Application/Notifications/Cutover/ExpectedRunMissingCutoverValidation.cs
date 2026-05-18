namespace Apperture.Core.Application.Notifications.Cutover;

/// <summary>Read-only staging cutover validation for a single integration.</summary>
public sealed record ExpectedRunMissingCutoverValidation(
    string IntegrationId,
    IReadOnlyList<string> OperatingModes,
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
