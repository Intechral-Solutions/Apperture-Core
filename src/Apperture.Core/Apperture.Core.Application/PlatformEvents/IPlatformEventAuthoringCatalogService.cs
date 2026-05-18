namespace Apperture.Core.Application.PlatformEvents;

/// <summary>Read-only platform/module event metadata for notification definition authoring (Epic 77 Phase 8A).</summary>
public interface IPlatformEventAuthoringCatalogService
{
    ValueTask<IReadOnlyList<PlatformEventAuthoringCatalogEntry>> ListAsync(CancellationToken cancellationToken = default);

    ValueTask<PlatformEventAuthoringCatalogEntry?> GetAsync(string eventType, CancellationToken cancellationToken = default);
}

public sealed record PlatformEventAuthoringCatalogEntry(
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
