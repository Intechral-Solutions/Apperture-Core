using System.Text.Json;
using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

public sealed record ParsedRuleState(
    string? EventType,
    string? SeverityOperator,
    string? SeverityValue,
    string? ModuleId,
    IReadOnlyList<NotificationPropertyFilterRow> PropertyFilters);

public static class NotificationRuleDocumentBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Build(
        string integrationId,
        string eventType,
        string? severityOperator,
        string? severityValue,
        string? moduleId,
        IReadOnlyList<NotificationPropertyFilterRow> propertyFilters)
    {
        var indexFilters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["integrationId"] = new Dictionary<string, object?>
            {
                ["operator"] = "equals",
                ["value"] = integrationId.Trim(),
            },
            ["type"] = new Dictionary<string, object?>
            {
                ["operator"] = "in",
                ["values"] = new[] { eventType.Trim() },
            },
        };

        if (!string.IsNullOrWhiteSpace(severityOperator) && !string.IsNullOrWhiteSpace(severityValue))
        {
            indexFilters["severity"] = BuildSeverityMatch(severityOperator, severityValue);
        }

        if (!string.IsNullOrWhiteSpace(moduleId))
        {
            indexFilters["moduleId"] = new Dictionary<string, object?>
            {
                ["operator"] = "equals",
                ["value"] = moduleId.Trim(),
            };
        }

        var stages = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["stage"] = "index",
                ["filters"] = indexFilters,
            },
        };

        foreach (var filter in propertyFilters.Where(f => !string.IsNullOrWhiteSpace(f.DataField)))
        {
            var path = filter.DataField.Trim().StartsWith("data.", StringComparison.Ordinal)
                ? filter.DataField.Trim()
                : $"data.{filter.DataField.Trim()}";
            stages.Add(new Dictionary<string, object?>
            {
                ["stage"] = "property",
                ["match"] = new Dictionary<string, object?>
                {
                    [path] = BuildPropertyMatch(filter.Operator, filter.Value),
                },
            });
        }

        var document = new Dictionary<string, object?>
        {
            ["schemaVersion"] = NotificationDefinitionAuthoringModel.RuleSchemaVersion,
            ["trigger"] = new Dictionary<string, object?>
            {
                ["eventTypes"] = new[] { eventType.Trim() },
            },
            ["stages"] = stages,
        };

        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static bool TryParse(string ruleJson, string ownerIntegrationId, out ParsedRuleState state)
    {
        state = new ParsedRuleState(null, null, null, null, []);
        try
        {
            using var doc = JsonDocument.Parse(ruleJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("schemaVersion", out var schema)
                && schema.GetString() != NotificationDefinitionAuthoringModel.RuleSchemaVersion)
                return false;

            string? eventType = null;
            if (root.TryGetProperty("trigger", out var trigger)
                && trigger.TryGetProperty("eventTypes", out var eventTypes)
                && eventTypes.GetArrayLength() > 0)
            {
                eventType = eventTypes[0].GetString();
            }

            string? severityOperator = null;
            string? severityValue = null;
            string? moduleId = null;
            var propertyFilters = new List<NotificationPropertyFilterRow>();

            if (root.TryGetProperty("stages", out var stages))
            {
                foreach (var stage in stages.EnumerateArray())
                {
                    var kind = stage.TryGetProperty("stage", out var stageKind) ? stageKind.GetString() : null;
                    if (kind == "index" && stage.TryGetProperty("filters", out var filters))
                    {
                        if (filters.TryGetProperty("severity", out var severityEl))
                            ParseSeverity(severityEl, out severityOperator, out severityValue);
                        if (filters.TryGetProperty("moduleId", out var moduleEl)
                            && moduleEl.TryGetProperty("value", out var moduleValue))
                            moduleId = moduleValue.GetString();
                    }
                    else if (kind == "property" && stage.TryGetProperty("match", out var match))
                    {
                        foreach (var prop in match.EnumerateObject())
                        {
                            if (!prop.Name.StartsWith("data.", StringComparison.Ordinal))
                                continue;
                            var field = prop.Name["data.".Length..];
                            var op = prop.Value.TryGetProperty("operator", out var opEl) ? opEl.GetString() : "equals";
                            var val = ExtractMatchValue(prop.Value);
                            propertyFilters.Add(new NotificationPropertyFilterRow
                            {
                                DataField = field,
                                Operator = op ?? "equals",
                                Value = val,
                            });
                        }
                    }
                    else if (kind == "expression")
                    {
                        return false;
                    }
                }
            }

            if (root.TryGetProperty("stages", out var stagesCheck))
            {
                foreach (var stage in stagesCheck.EnumerateArray())
                {
                    if (stage.TryGetProperty("stage", out var sk) && sk.GetString() == "index"
                        && stage.TryGetProperty("filters", out var filters)
                        && filters.TryGetProperty("integrationId", out var integ)
                        && integ.TryGetProperty("value", out var integVal))
                    {
                        var pinned = integVal.GetString();
                        if (!string.Equals(pinned, ownerIntegrationId, StringComparison.Ordinal))
                            return false;
                    }
                }
            }

            state = new ParsedRuleState(eventType, severityOperator, severityValue, moduleId, propertyFilters);
            return !string.IsNullOrWhiteSpace(eventType);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static IReadOnlyDictionary<string, string> BuildSampleEventData(PlatformEventCatalogEntryDto? catalogEntry)
    {
        if (catalogEntry is null || catalogEntry.RequiredDataProperties.Count == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in catalogEntry.RequiredDataProperties)
        {
            var key = ToCamelCaseDataKey(prop);
            data[key] = $"sample-{key}";
        }

        return data;
    }

    private static string ToCamelCaseDataKey(string prop)
    {
        if (string.IsNullOrEmpty(prop))
            return prop;
        if (char.IsLower(prop[0]))
            return prop;
        return char.ToLowerInvariant(prop[0]) + prop[1..];
    }

    private static object BuildSeverityMatch(string severityOperator, string severityValue)
    {
        if (string.Equals(severityOperator, "in", StringComparison.OrdinalIgnoreCase))
        {
            var values = severityValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new Dictionary<string, object?>
            {
                ["operator"] = "in",
                ["values"] = values,
            };
        }

        return new Dictionary<string, object?>
        {
            ["operator"] = "equals",
            ["value"] = severityValue.Trim(),
        };
    }

    private static object BuildPropertyMatch(string op, string value)
    {
        if (string.Equals(op, "in", StringComparison.OrdinalIgnoreCase))
        {
            var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new Dictionary<string, object?> { ["operator"] = "in", ["values"] = values };
        }

        return new Dictionary<string, object?> { ["operator"] = op.Trim(), ["value"] = value.Trim() };
    }

    private static void ParseSeverity(JsonElement severityEl, out string? op, out string? value)
    {
        op = null;
        value = null;
        if (severityEl.ValueKind == JsonValueKind.Array)
        {
            op = "in";
            value = string.Join(", ", severityEl.EnumerateArray().Select(e => e.GetString()).Where(s => s is not null));
            return;
        }

        if (severityEl.TryGetProperty("operator", out var opEl))
            op = opEl.GetString();
        if (severityEl.TryGetProperty("value", out var valEl))
            value = valEl.GetString();
        else if (severityEl.TryGetProperty("values", out var valsEl))
            value = string.Join(", ", valsEl.EnumerateArray().Select(e => e.GetString()).Where(s => s is not null));
    }

    private static string ExtractMatchValue(JsonElement match)
    {
        if (match.TryGetProperty("value", out var val))
            return val.ToString();
        if (match.TryGetProperty("values", out var vals))
            return string.Join(", ", vals.EnumerateArray().Select(e => e.GetString()).Where(s => s is not null));
        return "";
    }
}
