namespace Apperture.Management.Client;

public sealed record PlatformEventCatalogEntryDto(
    string Type,
    string Source,
    string DisplayName,
    string? Description,
    IReadOnlyList<string> AllowedSeverities,
    bool RequiresIntegrationId,
    bool RequiresScheduleId,
    bool RequiresRunId,
    IReadOnlyList<string> RequiredDataProperties,
    IReadOnlyList<string> TemplateVariables);
