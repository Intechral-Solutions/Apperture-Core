namespace Apperture.Core.Application.Integration;

/// <summary>
/// Request to create an integration. Epic 49.
/// </summary>
public sealed record CreateIntegrationRequest(
    string DisplayName,
    string ModuleId,
    string? ScopeKey = null);
