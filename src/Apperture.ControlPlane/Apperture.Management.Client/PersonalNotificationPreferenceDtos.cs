namespace Apperture.Management.Client;

public sealed class PersonalNotificationEmailPreferenceDto
{
    public string DefinitionId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? Description { get; init; }
    public bool EmailEnabled { get; init; }
    public bool HasSelfSubscription { get; init; }
    public bool HasGlobalEmailOptOut { get; init; }
}

public sealed class SetPersonalNotificationEmailPreferenceRequest
{
    public bool EmailEnabled { get; init; }
}
