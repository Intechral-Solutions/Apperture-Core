using System.Text.Json.Serialization;

namespace Apperture.Core.Application.Notifications;

/// <summary>apperture.notification.rule/v1 document.</summary>
public sealed class NotificationRuleDocument
{
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; set; }

    [JsonPropertyName("trigger")]
    public NotificationRuleTrigger? Trigger { get; set; }

    [JsonPropertyName("stages")]
    public IReadOnlyList<NotificationRuleStage>? Stages { get; set; }

    [JsonPropertyName("throttle")]
    public NotificationRuleThrottle? Throttle { get; set; }

    [JsonPropertyName("requiresIncident")]
    public bool? RequiresIncident { get; set; }

    [JsonPropertyName("priority")]
    public int? Priority { get; set; }
}

public sealed class NotificationRuleTrigger
{
    [JsonPropertyName("eventTypes")]
    public IReadOnlyList<string>? EventTypes { get; set; }

    [JsonPropertyName("incidentTransitions")]
    public IReadOnlyList<string>? IncidentTransitions { get; set; }

    [JsonPropertyName("notificationKinds")]
    public IReadOnlyList<string>? NotificationKinds { get; set; }
}

public sealed class NotificationRuleStage
{
    [JsonPropertyName("stage")]
    public string? Stage { get; set; }

    [JsonPropertyName("filters")]
    public IReadOnlyDictionary<string, NotificationRuleMatch>? Filters { get; set; }

    [JsonPropertyName("match")]
    public IReadOnlyDictionary<string, NotificationRuleMatch>? Match { get; set; }

    [JsonPropertyName("expression")]
    public string? Expression { get; set; }
}

public sealed class NotificationRuleMatch
{
    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("values")]
    public IReadOnlyList<object>? Values { get; set; }
}

public sealed class NotificationRuleThrottle
{
    [JsonPropertyName("window")]
    public string? Window { get; set; }

    [JsonPropertyName("maxPerWindow")]
    public int? MaxPerWindow { get; set; }

    [JsonPropertyName("perSubjectMinInterval")]
    public string? PerSubjectMinInterval { get; set; }

    [JsonPropertyName("perRecipientMinInterval")]
    public string? PerRecipientMinInterval { get; set; }
}
