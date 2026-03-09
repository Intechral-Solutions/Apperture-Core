using Apperture.Core.Domain.Environment;

namespace Apperture.Core.Domain.Trust;

/// <summary>
/// Catalog entry for a remote Environment known to the local control plane.
/// </summary>
public sealed record RemoteEnvironment(
    EnvironmentId Id,
    string DisplayName,
    string? OrganizationName,
    string? PublicId,
    string Status,
    DateTimeOffset? LastSeenAtUtc);
