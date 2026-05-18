namespace Apperture.Core.Application.Notifications.Cutover;

/// <summary>Operator review record for expected-run-missing platform notification cutover.</summary>
public sealed record ExpectedRunMissingCutoverSignOff(
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
