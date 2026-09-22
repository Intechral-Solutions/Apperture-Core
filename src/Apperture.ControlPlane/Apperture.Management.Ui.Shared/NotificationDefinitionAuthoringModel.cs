using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

public enum NotificationDefinitionAuthoringMode
{
    Create,
    Edit,
}

public sealed class NotificationPropertyFilterRow
{
    public string DataField { get; set; } = "";
    public string Operator { get; set; } = "equals";
    public string Value { get; set; } = "";
}

public sealed class NotificationDefinitionAuthoringModel
{
    public const string RuleSchemaVersion = "apperture.notification.rule/v1";
    public const string TemplateSchemaVersion = "apperture.notification.template/v1";

    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string IntegrationId { get; set; } = "";
    public string? SelectedEventType { get; set; }
    public string? DefaultSeverity { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool ToastEnabledByDefault { get; set; }

    public string? IndexSeverityOperator { get; set; }
    public string? IndexSeverityValue { get; set; }
    public string? IndexModuleId { get; set; }
    public List<NotificationPropertyFilterRow> PropertyFilters { get; } = [];

    public string Subject { get; set; } = "";
    public string Summary { get; set; } = "";
    public string BodyText { get; set; } = "";
    public string? ActionText { get; set; }
    public string? ActionUrl { get; set; }
    public List<NotificationTemplateContentFieldRow> ContentFields { get; } = [];

    public bool UseAdvancedRuleJson { get; set; }
    public string AdvancedRuleJson { get; set; } = "";
    public bool UseAdvancedTemplateJson { get; set; }
    public string AdvancedTemplateJson { get; set; } = "";

    public PlatformEventCatalogEntryDto? SelectedCatalogEntry { get; set; }

    public void ApplyFromDetail(NotificationDefinitionDetailDto detail)
    {
        DisplayName = detail.DisplayName;
        Description = detail.Description;
        IntegrationId = detail.IntegrationId ?? "";
        SelectedEventType = detail.PrimaryEventType;
        DefaultSeverity = detail.DefaultSeverity;
        IsEnabled = detail.IsEnabled;
        ToastEnabledByDefault = detail.ToastEnabledByDefault;

        if (NotificationRuleDocumentBuilder.TryParse(detail.RuleJson, IntegrationId, out var ruleState))
        {
            SelectedEventType = ruleState.EventType ?? SelectedEventType;
            IndexSeverityOperator = ruleState.SeverityOperator;
            IndexSeverityValue = ruleState.SeverityValue;
            IndexModuleId = ruleState.ModuleId;
            PropertyFilters.Clear();
            PropertyFilters.AddRange(ruleState.PropertyFilters);
            UseAdvancedRuleJson = false;
        }
        else
        {
            UseAdvancedRuleJson = true;
            AdvancedRuleJson = detail.RuleJson;
        }

        if (NotificationTemplateDocumentBuilder.TryParse(detail.TemplateJson, out var templateState))
        {
            Subject = templateState.Subject;
            Summary = templateState.Summary;
            BodyText = templateState.BodyText;
            ActionText = templateState.ActionText;
            ActionUrl = templateState.ActionUrl;
            ContentFields.Clear();
            ContentFields.AddRange(templateState.ContentFields);
            UseAdvancedTemplateJson = false;
        }
        else
        {
            UseAdvancedTemplateJson = true;
            AdvancedTemplateJson = detail.TemplateJson;
        }
    }

    public string BuildRuleJson()
    {
        if (UseAdvancedRuleJson)
            return AdvancedRuleJson;

        if (string.IsNullOrWhiteSpace(SelectedEventType))
            throw new InvalidOperationException("Event type is required.");

        return NotificationRuleDocumentBuilder.Build(
            IntegrationId,
            SelectedEventType,
            IndexSeverityOperator,
            IndexSeverityValue,
            IndexModuleId,
            PropertyFilters);
    }

    public string BuildTemplateJson()
    {
        if (UseAdvancedTemplateJson)
            return AdvancedTemplateJson;

        return NotificationTemplateDocumentBuilder.Build(
            Subject,
            Summary,
            BodyText,
            ActionText,
            ActionUrl,
            ContentFields);
    }

    public NotificationRenderSampleDto BuildRenderSample(string? integrationDisplayName)
    {
        var eventType = SelectedEventType ?? PlatformEventCatalogEntryDtoFallback.EventType;
        var severity = DefaultSeverity
                       ?? SelectedCatalogEntry?.AllowedSeverities.FirstOrDefault()
                       ?? "warning";
        var eventData = NotificationRuleDocumentBuilder.BuildSampleEventData(SelectedCatalogEntry);

        return new NotificationRenderSampleDto(
            eventType,
            DateTimeOffset.UtcNow,
            severity,
            IntegrationId,
            integrationDisplayName,
            "activation",
            null,
            null,
            null,
            eventData);
    }
}

public sealed class NotificationTemplateContentFieldRow
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}

internal static class PlatformEventCatalogEntryDtoFallback
{
    public const string EventType = "apperture.schedule.expected_run.missing";
}
