using System.Globalization;
using System.Text.Json;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications;

public sealed class NotificationRuleEvaluator : INotificationRuleEvaluator
{
    private static readonly HashSet<string> IndexFields = new(StringComparer.Ordinal)
    {
        "type", "severity", "integrationId", "moduleId", "runId", "scheduleId", "time", "subject",
    };

    private readonly INotificationTemplateVariableCatalog _variables;
    private readonly INotificationThrottleStore _throttle;
    private readonly INotificationRenderContextBuilder _renderContextBuilder;

    public NotificationRuleEvaluator(
        INotificationTemplateVariableCatalog variables,
        INotificationThrottleStore throttle,
        INotificationRenderContextBuilder renderContextBuilder)
    {
        _variables = variables;
        _throttle = throttle;
        _renderContextBuilder = renderContextBuilder;
    }

    public async ValueTask<IReadOnlyList<NotificationMatchResult>> EvaluateAllAsync(
        IReadOnlyList<NotificationDefinition> definitions,
        NotificationRuleEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        var results = new List<NotificationMatchResult>();
        foreach (var definition in definitions)
        {
            if (!definition.IsEnabled)
                continue;

            var kinds = NotificationKindMapper.MapNotificationKinds(context);
            if (kinds.Count == 0)
            {
                var evaluated = await EvaluateDefinitionAsync(definition, context, cancellationToken)
                    .ConfigureAwait(false);
                results.Add(ToMatchResult(evaluated));
                continue;
            }

            foreach (var kind in kinds)
            {
                var evaluated = await EvaluateDefinitionForKindAsync(
                        definition, context, kind, cancellationToken)
                    .ConfigureAwait(false);
                results.Add(ToMatchResult(evaluated));
            }
        }

        return results;
    }

    public async ValueTask<NotificationRuleEvaluationResult> EvaluateDefinitionAsync(
        NotificationDefinition definition,
        NotificationRuleEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<string>();
        if (!NotificationDocumentJson.TryDeserializeRule(definition.RuleJson, out var rule, out var parseError))
            return NoMatch(definition.Id.Value, diagnostics, parseError ?? "Invalid rule JSON.");

        if (rule!.SchemaVersion != NotificationSchemaVersions.RuleV1)
            return NoMatch(definition.Id.Value, diagnostics, "Invalid schema version.");

        if (!MatchesTrigger(rule, context, diagnostics))
            return NoMatch(definition.Id.Value, diagnostics, "Trigger mismatch.");

        var envelope = context.EffectivePlatformEvent;
        if (!EvaluateStages(rule, envelope, context.EvaluatedAtUtc, diagnostics))
            return NoMatch(definition.Id.Value, diagnostics, "Stage mismatch.");

        if (rule.RequiresIncident == true && !HasIncident(context, envelope))
            return NoMatch(definition.Id.Value, diagnostics, "requiresIncident: no incident available.");

        var kind = NotificationKindMapper.MapNotificationKind(context);
        if (kind is null)
            return NoMatch(definition.Id.Value, diagnostics, "No notification kind mapping.");

        return await CompleteMatchAsync(definition, context, rule, envelope, kind, diagnostics, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<NotificationRuleEvaluationResult> EvaluateDefinitionForKindAsync(
        NotificationDefinition definition,
        NotificationRuleEvaluationContext context,
        string kind,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        if (!NotificationDocumentJson.TryDeserializeRule(definition.RuleJson, out var rule, out var parseError))
            return NoMatch(definition.Id.Value, diagnostics, parseError ?? "Invalid rule JSON.");

        if (rule!.SchemaVersion != NotificationSchemaVersions.RuleV1)
            return NoMatch(definition.Id.Value, diagnostics, "Invalid schema version.");

        if (!MatchesTrigger(rule, context, diagnostics))
            return NoMatch(definition.Id.Value, diagnostics, "Trigger mismatch.");

        var envelope = context.EffectivePlatformEvent;
        if (!EvaluateStages(rule, envelope, context.EvaluatedAtUtc, diagnostics))
            return NoMatch(definition.Id.Value, diagnostics, "Stage mismatch.");

        if (rule.RequiresIncident == true && !HasIncident(context, envelope))
            return NoMatch(definition.Id.Value, diagnostics, "requiresIncident: no incident available.");

        return await CompleteMatchAsync(definition, context, rule, envelope, kind, diagnostics, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<NotificationRuleEvaluationResult> CompleteMatchAsync(
        NotificationDefinition definition,
        NotificationRuleEvaluationContext context,
        NotificationRuleDocument rule,
        PlatformEventEnvelope envelope,
        string kind,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!NotificationKindMapper.IsAllowedForSource(kind, context))
            return NoMatch(definition.Id.Value, diagnostics, "Notification kind not allowed for evaluation source.");

        if (rule.Trigger?.NotificationKinds is { Count: > 0 } allowedKinds &&
            !allowedKinds.Contains(kind, StringComparer.Ordinal))
            return NoMatch(definition.Id.Value, diagnostics, $"Notification kind '{kind}' not in trigger.notificationKinds.");

        var subjectKey = envelope.Subject ?? envelope.DedupeKey ?? envelope.Type;
        if (rule.Throttle?.PerSubjectMinInterval is { } intervalText &&
            NotificationIntervalParser.TryParse(intervalText, out var minInterval))
        {
            if (await _throttle.IsThrottledAsync(
                    definition.Id.Value,
                    subjectKey,
                    minInterval,
                    context.EvaluatedAtUtc,
                    cancellationToken).ConfigureAwait(false))
            {
                return Suppressed(definition.Id.Value, kind, "throttled");
            }
        }

        await _throttle.RecordMatchAsync(definition.Id.Value, subjectKey, context.EvaluatedAtUtc, cancellationToken)
            .ConfigureAwait(false);

        var renderContext = _renderContextBuilder.Build(
            envelope,
            kind,
            templateVariantKey: kind,
            incident: null);

        return new NotificationRuleEvaluationResult(
            definition.Id.Value,
            NotificationMatchStatus.Matched,
            kind,
            kind,
            null,
            diagnostics,
            renderContext);
    }

    private static bool MatchesTrigger(
        NotificationRuleDocument rule,
        NotificationRuleEvaluationContext context,
        List<string> diagnostics)
    {
        var trigger = rule.Trigger;
        if (trigger is null)
            return false;

        if (context.SourceKind == NotificationEvaluationSourceKind.PlatformEvent)
        {
            var envelope = context.PlatformEvent;
            if (envelope is null)
                return false;
            if (trigger.EventTypes is { Count: > 0 } &&
                !trigger.EventTypes.Contains(envelope.Type, StringComparer.Ordinal))
            {
                diagnostics.Add("eventType not in trigger.eventTypes");
                return false;
            }

            return true;
        }

        var incident = context.Incident;
        if (incident is null)
            return false;

        var transitionToken = NotificationIncidentTransitions.FromObserveTransition(incident.Transition);
        if (transitionToken is null)
        {
            diagnostics.Add("incident transition not mappable");
            return false;
        }

        if (trigger.IncidentTransitions is { Count: > 0 } &&
            !trigger.IncidentTransitions.Contains(transitionToken, StringComparer.Ordinal))
        {
            diagnostics.Add("incident transition not in trigger.incidentTransitions");
            return false;
        }

        return true;
    }

    private bool EvaluateStages(
        NotificationRuleDocument rule,
        PlatformEventEnvelope envelope,
        DateTimeOffset evaluatedAtUtc,
        List<string> diagnostics)
    {
        if (rule.Stages is not { Count: > 0 })
            return false;

        foreach (var stage in rule.Stages)
        {
            var kind = stage.Stage ?? "";
            if (kind == "index")
            {
                if (!EvaluateIndexStage(stage, envelope, evaluatedAtUtc))
                {
                    diagnostics.Add("index stage failed");
                    return false;
                }
            }
            else if (kind == "property")
            {
                if (!EvaluatePropertyStage(stage, envelope))
                {
                    diagnostics.Add("property stage failed");
                    return false;
                }
            }
            else if (kind == "expression")
            {
                diagnostics.Add("expression stage not evaluated in Phase 4");
                return false;
            }
        }

        return true;
    }

    private static bool EvaluateIndexStage(
        NotificationRuleStage stage,
        PlatformEventEnvelope envelope,
        DateTimeOffset evaluatedAtUtc)
    {
        if (stage.Filters is not { Count: > 0 })
            return false;

        foreach (var (field, match) in stage.Filters)
        {
            if (!IndexFields.Contains(field))
                return false;
            if (match is null || string.IsNullOrWhiteSpace(match.Operator))
                return false;
            if (!EvaluateIndexFilter(field, match, envelope, evaluatedAtUtc))
                return false;
        }

        return true;
    }

    private static bool EvaluateIndexFilter(
        string field,
        NotificationRuleMatch match,
        PlatformEventEnvelope envelope,
        DateTimeOffset evaluatedAtUtc)
    {
        var op = match.Operator!;
        return field switch
        {
            "type" => EvaluateStringOp(op, envelope.Type, match),
            "severity" => EvaluateStringOp(op, envelope.Severity, match),
            "integrationId" => EvaluateStringOp(op, envelope.IntegrationId, match),
            "moduleId" => EvaluateStringOp(op, envelope.ModuleId, match),
            "runId" => EvaluateStringOp(op, envelope.RunId, match),
            "scheduleId" => EvaluateStringOp(op, envelope.ScheduleId, match),
            "subject" => EvaluateStringOp(op, envelope.Subject, match),
            "time" => op == "withinLookback"
                && EvaluateTimeOp(op, envelope.OccurredAtUtc, match, evaluatedAtUtc),
            _ => false,
        };
    }

    private static bool EvaluateStringOp(string op, string? actual, NotificationRuleMatch match) =>
        op switch
        {
            "present" => !string.IsNullOrWhiteSpace(actual),
            "equals" => string.Equals(actual, MatchValueToString(match.Value), StringComparison.Ordinal),
            "in" => match.Values?.Select(MatchValueToString)
                .Any(v => string.Equals(v, actual, StringComparison.Ordinal)) == true,
            _ => false,
        };

    private static bool EvaluateTimeOp(
        string op,
        DateTimeOffset occurredAtUtc,
        NotificationRuleMatch match,
        DateTimeOffset evaluatedAtUtc)
    {
        if (op != "withinLookback")
            return false;
        if (!NotificationIntervalParser.TryParse(MatchValueToString(match.Value), out var lookback))
            return false;
        return evaluatedAtUtc - occurredAtUtc <= lookback;
    }

    private bool EvaluatePropertyStage(NotificationRuleStage stage, PlatformEventEnvelope envelope)
    {
        if (stage.Match is not { Count: > 0 })
            return false;

        using var doc = JsonDocument.Parse(envelope.DataJson);
        var allowlist = _variables.GetAllowlist(envelope.Type);

        foreach (var (path, match) in stage.Match)
        {
            if (!path.StartsWith("data.", StringComparison.Ordinal))
                return false;
            var templatePath = "event." + path;
            if (!allowlist.Contains(templatePath))
                return false;
            if (match is null || string.IsNullOrWhiteSpace(match.Operator))
                return false;

            var segment = path["data.".Length..];
            if (!TryGetDataValue(doc.RootElement, segment, out var actual))
                return false;

            if (!EvaluatePropertyOp(match.Operator, actual, match))
                return false;
        }

        return true;
    }

    private static bool TryGetDataValue(JsonElement root, string camelPath, out JsonElement value)
    {
        value = root;
        if (root.ValueKind != JsonValueKind.Object)
            return false;
        if (!root.TryGetProperty(camelPath, out value))
        {
            var pascal = char.ToUpperInvariant(camelPath[0]) + camelPath[1..];
            if (!root.TryGetProperty(pascal, out value))
                return false;
        }

        return true;
    }

    private static bool EvaluatePropertyOp(string op, JsonElement actual, NotificationRuleMatch match)
    {
        return op switch
        {
            "equals" => CompareEquals(actual, match.Value),
            "neq" => !CompareEquals(actual, match.Value),
            "in" => match.Values?.Any(v => CompareEquals(actual, v)) == true,
            "gte" => CompareNumeric(actual, match.Value, (a, b) => a >= b),
            "lte" => CompareNumeric(actual, match.Value, (a, b) => a <= b),
            _ => false,
        };
    }

    private static bool CompareEquals(JsonElement actual, object? expected)
    {
        var expectedText = MatchValueToString(expected);
        return actual.ValueKind switch
        {
            JsonValueKind.String => string.Equals(actual.GetString(), expectedText, StringComparison.Ordinal),
            JsonValueKind.Number => double.TryParse(expectedText, NumberStyles.Any, CultureInfo.InvariantCulture, out var expNum)
                && actual.GetDouble().Equals(expNum),
            JsonValueKind.True or JsonValueKind.False => bool.TryParse(expectedText, out var expBool)
                && actual.GetBoolean() == expBool,
            _ => string.Equals(actual.ToString(), expectedText, StringComparison.Ordinal),
        };
    }

    private static bool CompareNumeric(JsonElement actual, object? expected, Func<double, double, bool> compare)
    {
        if (actual.ValueKind != JsonValueKind.Number)
            return false;
        if (!double.TryParse(MatchValueToString(expected), NumberStyles.Any, CultureInfo.InvariantCulture, out var exp))
            return false;
        return compare(actual.GetDouble(), exp);
    }

    private static bool HasIncident(NotificationRuleEvaluationContext context, PlatformEventEnvelope envelope)
    {
        if (!string.IsNullOrWhiteSpace(context.Incident?.IncidentId))
            return true;
        try
        {
            using var doc = JsonDocument.Parse(envelope.DataJson);
            if (doc.RootElement.TryGetProperty(PlatformEventDataProperties.AttentionIncidentId, out var prop) &&
                !string.IsNullOrWhiteSpace(prop.GetString()))
                return true;
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static string? MatchValueToString(object? value)
    {
        if (value is null)
            return null;
        if (value is JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => el.ToString(),
            };
        }

        return value.ToString();
    }

    private static NotificationRuleEvaluationResult NoMatch(
        string definitionId,
        List<string> diagnostics,
        string reason)
    {
        diagnostics.Add(reason);
        return new NotificationRuleEvaluationResult(
            definitionId,
            NotificationMatchStatus.NoMatch,
            null,
            null,
            null,
            diagnostics);
    }

    private static NotificationRuleEvaluationResult Suppressed(string definitionId, string kind, string reason) =>
        new(definitionId, NotificationMatchStatus.Suppressed, kind, kind, reason, []);

    private static NotificationMatchResult ToMatchResult(NotificationRuleEvaluationResult result) =>
        new(
            result.DefinitionId,
            result.Status,
            result.NotificationKind,
            result.TemplateVariantKey,
            result.SuppressionReason,
            result.RenderContext);
}
