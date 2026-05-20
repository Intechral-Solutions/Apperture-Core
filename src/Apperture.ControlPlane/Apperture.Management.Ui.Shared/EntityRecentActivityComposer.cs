using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Epic 94: Maps existing run/audit/notification API records into entity-local activity rows.
/// No invented data — only fields present on source DTOs.
/// </summary>
public static class EntityRecentActivityComposer
{
    public const int DefaultMaxItems = 10;
    public const int PerSourceTake = 5;
    public static readonly TimeSpan DefaultLookback = TimeSpan.FromDays(7);

    public static DateTimeOffset DefaultFromUtc() =>
        DateTimeOffset.UtcNow.Subtract(DefaultLookback);

    public static EntityRecentActivityItem FromRun(RunSummaryDto run)
    {
        var summary = string.IsNullOrWhiteSpace(run.ProcedureId)
            ? run.ExecutionDefinitionId
            : $"{run.ExecutionDefinitionId} · {run.ProcedureId}";
        return new EntityRecentActivityItem(
            run.CreatedAt,
            "Run",
            run.ExecutionKind,
            summary,
            Actor: null,
            StatusOrOutcome: run.Status,
            DrillInHref: $"/runs/{Uri.EscapeDataString(run.RunId)}",
            DrillInLabel: "View run");
    }

    public static EntityRecentActivityItem FromAudit(AuditTimelineEntryDto entry, string moduleIdForAuditLink)
    {
        var (href, label) = ResolveAuditDrillIn(entry, moduleIdForAuditLink);
        return new EntityRecentActivityItem(
            entry.Timestamp,
            "Audit",
            entry.Category,
            string.IsNullOrWhiteSpace(entry.Summary) ? entry.Action : entry.Summary,
            entry.Actor,
            string.IsNullOrWhiteSpace(entry.Outcome) ? null : entry.Outcome,
            href,
            label);
    }

    public static EntityRecentActivityItem FromNotification(NotificationInstanceSummaryDto instance)
    {
        return new EntityRecentActivityItem(
            instance.CreatedAtUtc,
            "Notification",
            instance.NotificationKind,
            instance.NotificationDefinitionId,
            Actor: null,
            StatusOrOutcome: instance.Status,
            DrillInHref: $"/config/notifications/instances/{Uri.EscapeDataString(instance.Id)}",
            DrillInLabel: "View instance");
    }

    public static IReadOnlyList<EntityRecentActivityItem> MergeAndTake(
        IEnumerable<EntityRecentActivityItem> items,
        int maxItems = DefaultMaxItems) =>
        items
            .OrderByDescending(i => i.TimestampUtc)
            .Take(maxItems)
            .ToList();

    public static ManagementStatusSemantic MapStatusSemantic(string? statusOrOutcome)
    {
        if (string.IsNullOrWhiteSpace(statusOrOutcome))
            return ManagementStatusSemantic.Neutral;

        var s = statusOrOutcome.Trim();
        if (s.Contains("fail", StringComparison.OrdinalIgnoreCase)
            || s.Contains("error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "Denied", StringComparison.OrdinalIgnoreCase))
            return ManagementStatusSemantic.Error;

        if (s.Contains("success", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "Succeeded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "Completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "Active", StringComparison.OrdinalIgnoreCase))
            return ManagementStatusSemantic.Success;

        if (s.Contains("warn", StringComparison.OrdinalIgnoreCase)
            || s.Contains("pending", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "Running", StringComparison.OrdinalIgnoreCase))
            return ManagementStatusSemantic.Warning;

        return ManagementStatusSemantic.Neutral;
    }

    private static (string? Href, string Label) ResolveAuditDrillIn(
        AuditTimelineEntryDto entry,
        string moduleIdForAuditLink)
    {
        foreach (var r in entry.RelatedReferences)
        {
            if (string.Equals(r.Kind, "run", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(r.Id))
                return ($"/runs/{Uri.EscapeDataString(r.Id)}", "View run");
            if (string.Equals(r.Kind, "schedule", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(r.Id))
                return ($"/schedules/{Uri.EscapeDataString(r.Id)}", "View schedule");
        }

        if (!string.IsNullOrWhiteSpace(moduleIdForAuditLink))
            return (Epic33UiDisplayHelpers.GetAuditLinkForModule(moduleIdForAuditLink), "View audit");

        return ("/control/audit", "View audit");
    }
}
