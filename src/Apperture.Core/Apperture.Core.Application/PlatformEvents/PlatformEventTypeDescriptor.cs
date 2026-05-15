namespace Apperture.Core.Application.PlatformEvents;

public sealed record PlatformEventTypeDescriptor(
    string Type,
    string DefaultSource,
    IReadOnlyList<string> AllowedSeverities,
    bool RequiresIntegrationId,
    bool RequiresScheduleId,
    IReadOnlyList<string> RequiredDataProperties,
    int MaxDataJsonLength,
    bool RequiresRunId = false);
