namespace Apperture.Persistence.SqlServer;

public sealed class NotificationUserInboxItemEntity
{
    public string Id { get; set; } = null!;
    public string NotificationInstanceId { get; set; } = null!;
    public string PrincipalUserId { get; set; } = null!;
    public string? DeliveryOutboxId { get; set; }
    public string SourceRecipientKind { get; set; } = null!;
    public string SourceRecipientRef { get; set; } = null!;
    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAtUtc { get; set; }
    public DateTimeOffset? FirstViewedAtUtc { get; set; }
    public DateTimeOffset? LastViewedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
