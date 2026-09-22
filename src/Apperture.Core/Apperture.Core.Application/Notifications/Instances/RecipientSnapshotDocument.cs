namespace Apperture.Core.Application.Notifications.Instances;

public sealed class RecipientSnapshotDocument
{
    public List<RecipientSnapshotEntry> Deliverable { get; set; } = [];
    public List<RecipientSnapshotEntry> Skipped { get; set; } = [];
    public List<RecipientSnapshotEntry> InApp { get; set; } = [];
}

public sealed class RecipientSnapshotEntry
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
