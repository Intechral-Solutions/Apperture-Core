using System.Text.Json.Serialization;

namespace Apperture.Core.Application.Notifications;

/// <summary>apperture.notification.template/v1 document.</summary>
public sealed class NotificationTemplateDocument
{
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("bodyText")]
    public string? BodyText { get; set; }

    [JsonPropertyName("bodyHtml")]
    public string? BodyHtml { get; set; }

    [JsonPropertyName("actionText")]
    public string? ActionText { get; set; }

    [JsonPropertyName("actionUrl")]
    public string? ActionUrl { get; set; }

    [JsonPropertyName("contentFields")]
    public IReadOnlyList<NotificationTemplateContentField>? ContentFields { get; set; }

    [JsonPropertyName("variants")]
    public IReadOnlyDictionary<string, NotificationTemplateVariant>? Variants { get; set; }
}

public sealed class NotificationTemplateVariant
{
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("bodyText")]
    public string? BodyText { get; set; }

    [JsonPropertyName("bodyHtml")]
    public string? BodyHtml { get; set; }

    [JsonPropertyName("actionText")]
    public string? ActionText { get; set; }

    [JsonPropertyName("actionUrl")]
    public string? ActionUrl { get; set; }

    [JsonPropertyName("contentFields")]
    public IReadOnlyList<NotificationTemplateContentField>? ContentFields { get; set; }
}

public sealed class NotificationTemplateContentField
{
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}
