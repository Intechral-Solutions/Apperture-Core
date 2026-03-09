namespace Apperture.Core.Domain.Environment;

/// <summary>
/// Execution/data-plane subsystem within an Environment. Entity.
/// </summary>
public sealed record RuntimePlane(
    RuntimePlaneId Id,
    EnvironmentId EnvironmentId,
    string DisplayName,
    string Status,
    string? SchedulerEndpoint);
