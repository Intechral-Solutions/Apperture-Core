namespace Apperture.Core.Domain.Environment;

/// <summary>
/// Module enabled in an Environment. Entity.
/// </summary>
public sealed record ModuleInstallation(
    ModuleInstallationId Id,
    EnvironmentId EnvironmentId,
    ModuleId ModuleId,
    string Version,
    bool Enabled,
    string? ConfigRef);
