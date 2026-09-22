namespace Apperture.Persistence.SqlServer;

public sealed class NotificationUserPreferenceEntity
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string NotificationDefinitionId { get; set; } = null!;
    public string Channel { get; set; } = null!;
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string? UpdatedByActorId { get; set; }
}
