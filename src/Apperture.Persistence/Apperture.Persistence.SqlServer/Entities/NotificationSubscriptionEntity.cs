namespace Apperture.Persistence.SqlServer;

public sealed class NotificationSubscriptionEntity
{
    public string Id { get; set; } = null!;
    public string NotificationDefinitionId { get; set; } = null!;
    public string? IntegrationId { get; set; }
    public string RecipientKind { get; set; } = null!;
    public string RecipientRef { get; set; } = null!;
    public string DeliveryMode { get; set; } = null!;
    public string Origin { get; set; } = null!;
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string? CreatedByActorId { get; set; }
    public string? UpdatedByActorId { get; set; }
}

public sealed class NotificationSubscriptionUserOverrideEntity
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string NotificationDefinitionId { get; set; } = null!;
    public string? OperationalTeamId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? CreatedByActorId { get; set; }
}
