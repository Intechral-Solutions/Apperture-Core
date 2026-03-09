namespace Apperture.Core.Domain.Environment;

/// <summary>
/// Full deployed platform boundary for a tenant or organization. Aggregate root.
/// </summary>
public sealed record Environment(
    EnvironmentId Id,
    string DisplayName,
    EnvironmentStatus Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RuntimePlaneId> RuntimePlaneIds);
