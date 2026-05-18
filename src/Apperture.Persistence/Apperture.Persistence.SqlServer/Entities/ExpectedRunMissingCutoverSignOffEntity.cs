namespace Apperture.Persistence.SqlServer;

public sealed class ExpectedRunMissingCutoverSignOffEntity
{
    public string Id { get; set; } = null!;
    public string IntegrationId { get; set; } = null!;
    public string NotificationDefinitionId { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset ReviewedAtUtc { get; set; }
    public string ReviewedByActorId { get; set; } = null!;
    public string? ReviewedByDisplayName { get; set; }
    public bool ParityAcceptableAtReview { get; set; }
    public string ConfigurationWarningCodesJson { get; set; } = "[]";
    public string BlockingFindingsJson { get; set; } = "[]";
    public string NonBlockingFindingsJson { get; set; } = "[]";
    public string? OperatorNote { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
