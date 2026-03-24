using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Integration;

/// <summary>
/// Request to update an integration (partial update). Epic 49.
/// </summary>
public sealed record UpdateIntegrationRequest(
    string? DisplayName = null,
    IntegrationState? State = null);
