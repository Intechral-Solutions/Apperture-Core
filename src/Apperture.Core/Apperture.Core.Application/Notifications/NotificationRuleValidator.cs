using System.Text.Json;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications;

public sealed class NotificationRuleValidator : INotificationRuleValidator
{
    private static readonly HashSet<string> IndexFields = new(StringComparer.Ordinal)
    {
        "type", "severity", "integrationId", "moduleId", "runId", "scheduleId", "time", "subject",
    };

    private static readonly HashSet<string> IndexOperators = new(StringComparer.Ordinal)
    {
        "in", "equals", "present", "withinLookback",
    };

    private static readonly HashSet<string> PropertyOperators = new(StringComparer.Ordinal)
    {
        "equals", "neq", "in", "gte", "lte",
    };

    private static readonly HashSet<string> StageKinds = new(StringComparer.Ordinal)
    {
        "index", "property", "expression",
    };

    private readonly IPlatformEventCatalog _catalog;
    private readonly INotificationTemplateVariableCatalog _variables;

    public NotificationRuleValidator(
        IPlatformEventCatalog catalog,
        INotificationTemplateVariableCatalog variables)
    {
        _catalog = catalog;
        _variables = variables;
    }

    public NotificationValidationResult Validate(string ruleJson, NotificationRuleValidationContext context)
    {
        if (!NotificationDocumentJson.TryDeserializeRule(ruleJson, out var document, out var parseError))
            return NotificationValidationResult.Failure(new NotificationValidationIssue("parse", parseError ?? "Invalid rule JSON."));

        var issues = new List<NotificationValidationIssue>();
        if (document!.SchemaVersion != NotificationSchemaVersions.RuleV1)
            issues.Add(new("schemaVersion", $"schemaVersion must be {NotificationSchemaVersions.RuleV1}."));

        var eventTypes = ResolveEventTypes(document, context);
        ValidateTrigger(document, eventTypes, issues);
        ValidateStages(document, context, eventTypes, issues);
        ValidateUserIntegrationPin(document, context, issues);

        return issues.Count == 0
            ? NotificationValidationResult.Success()
            : new NotificationValidationResult(false, issues);
    }

    private static IReadOnlyList<string> ResolveEventTypes(
        NotificationRuleDocument document,
        NotificationRuleValidationContext context)
    {
        if (context.EventTypes is { Count: > 0 })
            return context.EventTypes;
        return document.Trigger?.EventTypes?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList()
               ?? [];
    }

    private void ValidateTrigger(
        NotificationRuleDocument document,
        IReadOnlyList<string> eventTypes,
        List<NotificationValidationIssue> issues)
    {
        var trigger = document.Trigger;
        if (trigger is null)
        {
            issues.Add(new("trigger", "trigger is required."));
            return;
        }

        var hasEventTypes = trigger.EventTypes is { Count: > 0 };
        var hasIncidentTransitions = trigger.IncidentTransitions is { Count: > 0 };
        if (!hasEventTypes && !hasIncidentTransitions)
            issues.Add(new("trigger", "At least one of eventTypes or incidentTransitions is required."));

        if (hasEventTypes)
        {
            foreach (var eventType in trigger.EventTypes!)
            {
                if (!_catalog.IsKnownType(eventType))
                    issues.Add(new("trigger.eventTypes", $"Unknown event type '{eventType}'."));
            }
        }

        if (eventTypes.Count == 0 && hasIncidentTransitions)
            issues.Add(new("trigger.eventTypes", "eventTypes required when validating template variables for incident-driven rules."));

        if (trigger.IncidentTransitions is not null)
        {
            foreach (var transition in trigger.IncidentTransitions)
            {
                if (!NotificationIncidentTransitions.All.Contains(transition))
                    issues.Add(new("trigger.incidentTransitions", $"Unknown incident transition '{transition}'."));
            }
        }

        if (trigger.NotificationKinds is not null)
        {
            foreach (var kind in trigger.NotificationKinds)
            {
                if (!NotificationKinds.All.Contains(kind))
                    issues.Add(new("trigger.notificationKinds", $"Unknown notification kind '{kind}'."));
            }
        }

        ValidateThrottle(document.Throttle, issues);
    }

    private static void ValidateThrottle(NotificationRuleThrottle? throttle, List<NotificationValidationIssue> issues)
    {
        if (throttle is null)
            return;
        if (!NotificationIntervalParser.IsValidFormat(throttle.PerSubjectMinInterval))
            issues.Add(new("throttle.perSubjectMinInterval", "Invalid interval format (e.g. 0m, 15m, 1h, 7d)."));
        if (!NotificationIntervalParser.IsValidFormat(throttle.PerRecipientMinInterval))
            issues.Add(new("throttle.perRecipientMinInterval", "Invalid interval format (e.g. 0m, 15m, 1h, 7d)."));
    }

    private void ValidateStages(
        NotificationRuleDocument document,
        NotificationRuleValidationContext context,
        IReadOnlyList<string> eventTypes,
        List<NotificationValidationIssue> issues)
    {
        if (document.Stages is not { Count: > 0 })
        {
            issues.Add(new("stages", "At least one stage is required."));
            return;
        }

        var hasIndex = false;
        foreach (var stage in document.Stages)
        {
            var kind = stage.Stage ?? "";
            if (!StageKinds.Contains(kind))
            {
                issues.Add(new("stages", $"Unknown stage kind '{kind}'."));
                continue;
            }

            if (kind == "index")
            {
                hasIndex = true;
                ValidateIndexStage(stage, issues);
            }
            else if (kind == "property")
            {
                ValidatePropertyStage(stage, eventTypes, issues);
            }
            else if (kind == "expression")
            {
                if (string.IsNullOrWhiteSpace(stage.Expression))
                    issues.Add(new("stages.expression", "expression stage requires expression."));
                if (context.Origin == NotificationDefinitionOrigins.UserCreated)
                    issues.Add(new("stages.expression", "expression stage is not allowed for user-created definitions."));
            }
        }

        if (!hasIndex)
            issues.Add(new("stages", "An index stage is required."));
    }

    private static void ValidateIndexStage(NotificationRuleStage stage, List<NotificationValidationIssue> issues)
    {
        if (stage.Filters is not { Count: > 0 })
        {
            issues.Add(new("stages.index.filters", "index stage requires filters."));
            return;
        }

        foreach (var (field, match) in stage.Filters)
        {
            if (!IndexFields.Contains(field))
                issues.Add(new($"stages.index.filters.{field}", $"Unknown index field '{field}'."));
            if (match is null)
                continue;
            if (string.IsNullOrWhiteSpace(match.Operator))
                issues.Add(new($"stages.index.filters.{field}", "operator is required."));
            else if (!IndexOperators.Contains(match.Operator))
                issues.Add(new($"stages.index.filters.{field}", $"Unknown operator '{match.Operator}'."));
        }
    }

    private void ValidatePropertyStage(
        NotificationRuleStage stage,
        IReadOnlyList<string> eventTypes,
        List<NotificationValidationIssue> issues)
    {
        if (stage.Match is not { Count: > 0 })
        {
            issues.Add(new("stages.property.match", "property stage requires match."));
            return;
        }

        var allowlist = eventTypes.Count > 0
            ? _variables.GetUnionAllowlist(eventTypes)
            : new HashSet<string>(StringComparer.Ordinal);

        foreach (var (path, match) in stage.Match)
        {
            if (!path.StartsWith("data.", StringComparison.Ordinal))
                issues.Add(new($"stages.property.match.{path}", "Property paths must start with data."));
            var templatePath = "event." + path;
            if (eventTypes.Count > 0 && !allowlist.Contains(templatePath))
                issues.Add(new($"stages.property.match.{path}", $"Path '{path}' is not allowlisted for the selected event types."));

            if (match is null || string.IsNullOrWhiteSpace(match.Operator))
            {
                issues.Add(new($"stages.property.match.{path}", "operator is required."));
                continue;
            }

            if (!PropertyOperators.Contains(match.Operator))
                issues.Add(new($"stages.property.match.{path}", $"Unknown operator '{match.Operator}'."));
        }
    }

    private static void ValidateUserIntegrationPin(
        NotificationRuleDocument document,
        NotificationRuleValidationContext context,
        List<NotificationValidationIssue> issues)
    {
        if (context.Origin != NotificationDefinitionOrigins.UserCreated)
            return;
        if (string.IsNullOrWhiteSpace(context.OwnerIntegrationId))
        {
            issues.Add(new("integrationId", "Owner integration id is required for user-created definitions."));
            return;
        }

        var indexStage = document.Stages?.FirstOrDefault(s => s.Stage == "index");
        if (indexStage?.Filters is null ||
            !indexStage.Filters.TryGetValue("integrationId", out var pin) ||
            !string.Equals(pin.Operator, "equals", StringComparison.Ordinal) ||
            MatchValueToString(pin.Value) != context.OwnerIntegrationId)
        {
            issues.Add(new(
                "stages.index.filters.integrationId",
                $"User-created definitions must pin integrationId equals \"{context.OwnerIntegrationId}\"."));
        }
    }

    private static string? MatchValueToString(object? value)
    {
        if (value is null)
            return null;
        if (value is JsonElement el)
            return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
        return value.ToString();
    }
}
