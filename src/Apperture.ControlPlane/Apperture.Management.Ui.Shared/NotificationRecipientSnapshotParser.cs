using System.Text.Json;

namespace Apperture.Management.Ui.Shared;

public sealed class NotificationRecipientSnapshotModel
{
    public List<NotificationRecipientSnapshotEntry> Deliverable { get; set; } = [];
    public List<NotificationRecipientSnapshotEntry> Skipped { get; set; } = [];
}

public sealed class NotificationRecipientSnapshotEntry
{
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? UserId { get; set; }
    public string? ManagedContactId { get; set; }
    public string? Usability { get; set; }
    public string Source { get; set; } = "";
    public string? SubscriptionId { get; set; }
    public string? RecipientKind { get; set; }
    public string? Reason { get; set; }
}

public static class NotificationRecipientSnapshotParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static NotificationRecipientSnapshotModel? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<NotificationRecipientSnapshotModel>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
