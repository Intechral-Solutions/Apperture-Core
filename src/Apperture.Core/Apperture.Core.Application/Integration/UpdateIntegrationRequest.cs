using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Integration;

/// <summary>
/// Request to update an integration (partial update). Epic 49, Epic 50.
/// </summary>
public sealed record UpdateIntegrationRequest(
    string? DisplayName = null,
    IntegrationState? State = null,
    IReadOnlyDictionary<string, string>? Settings = null);
