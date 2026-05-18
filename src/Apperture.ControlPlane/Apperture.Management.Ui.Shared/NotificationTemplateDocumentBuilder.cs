using System.Text.Json;

namespace Apperture.Management.Ui.Shared;

public sealed record ParsedTemplateState(
    string Subject,
    string Summary,
    string BodyText,
    string? ActionText,
    string? ActionUrl,
    IReadOnlyList<NotificationTemplateContentFieldRow> ContentFields);

public static class NotificationTemplateDocumentBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Build(
        string subject,
        string summary,
        string bodyText,
        string? actionText,
        string? actionUrl,
        IReadOnlyList<NotificationTemplateContentFieldRow> contentFields)
    {
        var document = new Dictionary<string, object?>
        {
            ["schemaVersion"] = NotificationDefinitionAuthoringModel.TemplateSchemaVersion,
            ["subject"] = subject,
            ["summary"] = summary,
            ["bodyText"] = bodyText,
        };

        if (!string.IsNullOrWhiteSpace(actionText))
            document["actionText"] = actionText;
        if (!string.IsNullOrWhiteSpace(actionUrl))
            document["actionUrl"] = actionUrl;

        var rows = contentFields
            .Where(r => !string.IsNullOrWhiteSpace(r.Label) || !string.IsNullOrWhiteSpace(r.Value))
            .Select(r => new Dictionary<string, string?>
            {
                ["label"] = r.Label,
                ["value"] = r.Value,
            })
            .ToList();
        if (rows.Count > 0)
            document["contentFields"] = rows;

        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static bool TryParse(string templateJson, out ParsedTemplateState state)
    {
        state = new ParsedTemplateState("", "", "", null, null, []);
        try
        {
            using var doc = JsonDocument.Parse(templateJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("schemaVersion", out var schema)
                && schema.GetString() != NotificationDefinitionAuthoringModel.TemplateSchemaVersion)
                return false;

            var subject = root.TryGetProperty("subject", out var s) ? s.GetString() ?? "" : "";
            var summary = root.TryGetProperty("summary", out var su) ? su.GetString() ?? "" : "";
            var body = root.TryGetProperty("bodyText", out var b) ? b.GetString() ?? "" : "";
            var actionText = root.TryGetProperty("actionText", out var at) ? at.GetString() : null;
            var actionUrl = root.TryGetProperty("actionUrl", out var au) ? au.GetString() : null;

            if (root.TryGetProperty("variants", out _) && root.GetProperty("variants").EnumerateObject().Any())
                return false;

            var contentFields = new List<NotificationTemplateContentFieldRow>();
            if (root.TryGetProperty("contentFields", out var fields))
            {
                foreach (var field in fields.EnumerateArray())
                {
                    contentFields.Add(new NotificationTemplateContentFieldRow
                    {
                        Label = field.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "",
                        Value = field.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "",
                    });
                }
            }

            state = new ParsedTemplateState(subject, summary, body, actionText, actionUrl, contentFields);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
