using System.Text.Json;

namespace Apperture.Core.Application.Notifications;

public static class NotificationDocumentJson
{
    public const int MaxDocumentBytes = 65_536;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static bool TryDeserializeRule(string json, out NotificationRuleDocument? document, out string? error)
    {
        document = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Rule JSON is required.";
            return false;
        }

        if (json.Length > MaxDocumentBytes)
        {
            error = $"Rule JSON exceeds {MaxDocumentBytes} bytes.";
            return false;
        }

        try
        {
            document = JsonSerializer.Deserialize<NotificationRuleDocument>(json, Options);
            if (document is null)
            {
                error = "Rule JSON is empty.";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryDeserializeTemplate(string json, out NotificationTemplateDocument? document, out string? error)
    {
        document = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Template JSON is required.";
            return false;
        }

        if (json.Length > MaxDocumentBytes)
        {
            error = $"Template JSON exceeds {MaxDocumentBytes} bytes.";
            return false;
        }

        try
        {
            document = JsonSerializer.Deserialize<NotificationTemplateDocument>(json, Options);
            if (document is null)
            {
                error = "Template JSON is empty.";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string SerializeRule(NotificationRuleDocument document) =>
        JsonSerializer.Serialize(document, Options);

    public static string SerializeTemplate(NotificationTemplateDocument document) =>
        JsonSerializer.Serialize(document, Options);
}
