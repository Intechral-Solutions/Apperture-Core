using System.Text.Json;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.Notifications.Inbox;
using Apperture.Core.Domain.Notifications;

namespace Apperture.ControlPlane.Security;

public static class NotificationUserInboxRecipientExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<InboxPrincipalRecipient> ExtractPrincipalRecipients(string? recipientSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(recipientSnapshotJson))
            return [];

        RecipientSnapshotDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<RecipientSnapshotDocument>(recipientSnapshotJson, JsonOptions);
        }
        catch (JsonException)
        {
            return [];
        }

        if (doc is null)
            return [];

        var byUser = new Dictionary<string, InboxPrincipalRecipient>(StringComparer.Ordinal);

        foreach (var entry in doc.Deliverable)
        {
            if (string.IsNullOrWhiteSpace(entry.UserId))
                continue;

            var userId = entry.UserId.Trim();
            var kind = string.IsNullOrWhiteSpace(entry.RecipientKind)
                ? NotificationRecipientKinds.Principal
                : entry.RecipientKind;
            var recipientRef = ResolveRecipientRef(entry, kind);

            if (!byUser.TryGetValue(userId, out var existing))
            {
                byUser[userId] = new InboxPrincipalRecipient(userId, kind, recipientRef, entry.Email);
                continue;
            }

            if (RecipientPriority(kind) > RecipientPriority(existing.SourceRecipientKind))
                byUser[userId] = new InboxPrincipalRecipient(userId, kind, recipientRef, entry.Email);
        }

        return byUser.Values.ToList();
    }

    private static string ResolveRecipientRef(RecipientSnapshotEntry entry, string kind) =>
        kind switch
        {
            NotificationRecipientKinds.ManagedContact when !string.IsNullOrWhiteSpace(entry.ManagedContactId)
                => entry.ManagedContactId!,
            NotificationRecipientKinds.OperationalTeam when !string.IsNullOrWhiteSpace(entry.SubscriptionId)
                => entry.SubscriptionId!,
            _ => entry.UserId ?? entry.SubscriptionId ?? entry.Email ?? "",
        };

    private static int RecipientPriority(string kind) =>
        kind switch
        {
            NotificationRecipientKinds.Principal => 3,
            NotificationRecipientKinds.ManagedContact => 2,
            NotificationRecipientKinds.OperationalTeam => 1,
            _ => 0,
        };

    public sealed record InboxPrincipalRecipient(
        string UserId,
        string SourceRecipientKind,
        string SourceRecipientRef,
        string? Email);
}
