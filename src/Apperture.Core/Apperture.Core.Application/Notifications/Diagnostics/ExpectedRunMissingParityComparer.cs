using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;

namespace Apperture.Core.Application.Notifications.Diagnostics;

public static class ExpectedRunMissingParityComparer
{
    public static ExpectedRunMissingParityDiagnostics Build(
        string integrationId,
        string attentionIncidentId,
        IReadOnlyList<Epic69NotificationDiagnosticRow> epic69Rows,
        IReadOnlyList<NotificationInstanceDiagnosticRow> platformInstances,
        IReadOnlyList<NotificationOutboxDiagnosticRow> platformOutbox,
        DateTimeOffset generatedAtUtc)
    {
        var kinds = new[] { NotificationKinds.Activation, NotificationKinds.Reminder, NotificationKinds.Resolved };
        var byKind = kinds
            .Select(kind => CompareKind(kind, epic69Rows, platformInstances, platformOutbox))
            .ToList();

        return new ExpectedRunMissingParityDiagnostics(
            integrationId,
            attentionIncidentId,
            byKind,
            platformInstances,
            platformOutbox,
            epic69Rows,
            generatedAtUtc);
    }

    private static ExpectedRunMissingKindParity CompareKind(
        string platformKind,
        IReadOnlyList<Epic69NotificationDiagnosticRow> epic69Rows,
        IReadOnlyList<NotificationInstanceDiagnosticRow> platformInstances,
        IReadOnlyList<NotificationOutboxDiagnosticRow> platformOutbox)
    {
        var epic69Kind = MapToEpic69Kind(platformKind);
        var epic69ForKind = epic69Rows
            .Where(r => string.Equals(r.NotificationKind, epic69Kind, StringComparison.Ordinal))
            .ToList();
        var instance = platformInstances
            .Where(i => string.Equals(i.NotificationKind, platformKind, StringComparison.Ordinal))
            .OrderByDescending(i => i.CreatedAtUtc)
            .FirstOrDefault();
        var outboxForKind = platformOutbox
            .Where(o => string.Equals(o.NotificationKind, platformKind, StringComparison.Ordinal))
            .ToList();

        var epic69Recipients = epic69ForKind
            .Where(r => !string.IsNullOrWhiteSpace(r.Recipient))
            .Select(r => r.Recipient)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var platformRecipients = outboxForKind
            .Select(o => o.RecipientEmail)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (instance is not null
            && string.Equals(instance.Status, "suppressed", StringComparison.OrdinalIgnoreCase)
            && platformRecipients.Count == 0)
        {
            platformRecipients.Add("(suppressed_instance)");
        }

        var recipientComparisons = BuildRecipientComparisons(
            epic69ForKind,
            instance,
            outboxForKind,
            epic69Recipients,
            platformRecipients);

        var differenceKind = ClassifyKindDifference(
            epic69ForKind,
            instance,
            outboxForKind,
            epic69Recipients,
            platformRecipients,
            recipientComparisons);

        var summary = Summarize(differenceKind, epic69Recipients, platformRecipients, instance, outboxForKind);

        return new ExpectedRunMissingKindParity(
            platformKind,
            differenceKind,
            summary,
            epic69Recipients,
            platformRecipients,
            recipientComparisons,
            AggregateEpic69Status(epic69ForKind),
            instance?.Status,
            instance?.SuppressionReason);
    }

    private static IReadOnlyList<ParityRecipientComparison> BuildRecipientComparisons(
        IReadOnlyList<Epic69NotificationDiagnosticRow> epic69ForKind,
        NotificationInstanceDiagnosticRow? instance,
        IReadOnlyList<NotificationOutboxDiagnosticRow> outboxForKind,
        IReadOnlyList<string> epic69Recipients,
        IReadOnlyList<string> platformRecipients)
    {
        var all = epic69Recipients
            .Concat(platformRecipients.Where(r => r != "(suppressed_instance)"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase);

        var comparisons = new List<ParityRecipientComparison>();
        foreach (var email in all)
        {
            var epic = epic69ForKind
                .Where(r => string.Equals(r.Recipient, email, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefault();
            var outbox = outboxForKind
                .Where(o => string.Equals(o.RecipientEmail, email, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(o => o.UpdatedAtUtc)
                .FirstOrDefault();

            var diff = ClassifyRecipientDifference(epic, instance, outbox, email, platformRecipients);
            comparisons.Add(
                new ParityRecipientComparison(
                    email,
                    diff,
                    epic?.Status,
                    epic?.SuppressionReason,
                    instance?.Status,
                    outbox?.Status,
                    outbox?.RecipientSource));
        }

        return comparisons;
    }

    private static string ClassifyRecipientDifference(
        Epic69NotificationDiagnosticRow? epic,
        NotificationInstanceDiagnosticRow? instance,
        NotificationOutboxDiagnosticRow? outbox,
        string email,
        IReadOnlyList<string> platformRecipients)
    {
        var inEpic = epic is not null;
        var inPlatform = platformRecipients.Contains(email, StringComparer.OrdinalIgnoreCase)
                         || (outbox is not null && string.Equals(outbox.RecipientEmail, email, StringComparison.OrdinalIgnoreCase));

        if (!inEpic && inPlatform)
            return ParityDifferenceKinds.ExtraInPlatform;
        if (inEpic && !inPlatform)
        {
            if (instance is not null
                && string.Equals(instance.Status, "suppressed", StringComparison.OrdinalIgnoreCase))
                return ParityDifferenceKinds.SuppressedInPlatformOnly;
            return ParityDifferenceKinds.MissingInPlatform;
        }

        if (epic is not null
            && string.Equals(epic.Status, "Suppressed", StringComparison.OrdinalIgnoreCase)
            && instance is not null
            && string.Equals(instance.Status, "suppressed", StringComparison.OrdinalIgnoreCase))
            return ParityDifferenceKinds.Aligned;

        if (epic is not null
            && string.Equals(epic.Status, "Suppressed", StringComparison.OrdinalIgnoreCase)
            && (instance is null || !string.Equals(instance.Status, "suppressed", StringComparison.OrdinalIgnoreCase)))
            return ParityDifferenceKinds.SuppressedInEpic69Only;

        if (outbox is not null)
        {
            if (string.Equals(outbox.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
                return string.Equals(epic?.Status, "Succeeded", StringComparison.OrdinalIgnoreCase)
                    ? ParityDifferenceKinds.Aligned
                    : ParityDifferenceKinds.DeliverySucceededInPlatform;
            if (string.Equals(outbox.Status, "failed", StringComparison.OrdinalIgnoreCase))
                return ParityDifferenceKinds.DeliveryFailedInPlatform;
            if (string.Equals(outbox.Status, "pending", StringComparison.OrdinalIgnoreCase)
                || string.Equals(outbox.Status, "delivering", StringComparison.OrdinalIgnoreCase))
                return ParityDifferenceKinds.InstancePendingInPlatform;
        }

        if (epic is not null && outbox is null && instance is not null)
            return ParityDifferenceKinds.StatusMismatch;

        return ParityDifferenceKinds.Aligned;
    }

    private static string ClassifyKindDifference(
        IReadOnlyList<Epic69NotificationDiagnosticRow> epic69ForKind,
        NotificationInstanceDiagnosticRow? instance,
        IReadOnlyList<NotificationOutboxDiagnosticRow> outboxForKind,
        IReadOnlyList<string> epic69Recipients,
        IReadOnlyList<string> platformRecipients,
        IReadOnlyList<ParityRecipientComparison> recipientComparisons)
    {
        if (recipientComparisons.Any(c => c.DifferenceKind != ParityDifferenceKinds.Aligned))
        {
            if (recipientComparisons.Any(c => c.DifferenceKind == ParityDifferenceKinds.MissingInPlatform))
                return ParityDifferenceKinds.MissingInPlatform;
            if (recipientComparisons.Any(c => c.DifferenceKind == ParityDifferenceKinds.ExtraInPlatform))
                return ParityDifferenceKinds.ExtraInPlatform;
            if (recipientComparisons.Any(c => c.DifferenceKind == ParityDifferenceKinds.DeliveryFailedInPlatform))
                return ParityDifferenceKinds.DeliveryFailedInPlatform;
            if (recipientComparisons.Any(c => c.DifferenceKind == ParityDifferenceKinds.InstancePendingInPlatform))
                return ParityDifferenceKinds.InstancePendingInPlatform;
            return ParityDifferenceKinds.StatusMismatch;
        }

        if (epic69ForKind.Count == 0 && instance is null)
            return ParityDifferenceKinds.Aligned;

        if (epic69ForKind.All(r => string.Equals(r.Status, "Suppressed", StringComparison.OrdinalIgnoreCase))
            && instance is not null
            && string.Equals(instance.Status, "suppressed", StringComparison.OrdinalIgnoreCase))
            return ParityDifferenceKinds.Aligned;

        if (epic69Recipients.Count == 0 && platformRecipients.Count == 0
            && epic69ForKind.Count > 0 && instance is null)
            return ParityDifferenceKinds.SuppressedInEpic69Only;

        return ParityDifferenceKinds.Aligned;
    }

    private static string Summarize(
        string differenceKind,
        IReadOnlyList<string> epic69Recipients,
        IReadOnlyList<string> platformRecipients,
        NotificationInstanceDiagnosticRow? instance,
        IReadOnlyList<NotificationOutboxDiagnosticRow> outboxForKind)
    {
        if (differenceKind == ParityDifferenceKinds.Aligned)
            return "Epic 69 ledger and platform pipeline appear aligned for this kind.";

        return $"Epic69 recipients=[{string.Join(", ", epic69Recipients)}]; " +
               $"platform recipients=[{string.Join(", ", platformRecipients)}]; " +
               $"instance={instance?.Status ?? "none"}; " +
               $"outbox statuses=[{string.Join(", ", outboxForKind.Select(o => $"{o.RecipientEmail}:{o.Status}"))}].";
    }

    private static string? AggregateEpic69Status(IReadOnlyList<Epic69NotificationDiagnosticRow> rows)
    {
        if (rows.Count == 0)
            return null;
        if (rows.Any(r => string.Equals(r.Status, "Succeeded", StringComparison.OrdinalIgnoreCase)))
            return "Succeeded";
        if (rows.All(r => string.Equals(r.Status, "Suppressed", StringComparison.OrdinalIgnoreCase)))
            return "Suppressed";
        if (rows.Any(r => string.Equals(r.Status, "Failed", StringComparison.OrdinalIgnoreCase)))
            return "Failed";
        return rows[0].Status;
    }

    private static string MapToEpic69Kind(string platformKind) =>
        platformKind switch
        {
            NotificationKinds.Activation => AttentionNotificationKinds.Activated,
            NotificationKinds.Reminder => AttentionNotificationKinds.Reminder,
            NotificationKinds.Resolved => AttentionNotificationKinds.Resolved,
            _ => platformKind,
        };
}
