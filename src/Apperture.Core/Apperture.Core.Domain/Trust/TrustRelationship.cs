using Apperture.Core.Domain.Environment;

namespace Apperture.Core.Domain.Trust;

/// <summary>
/// Trust contract between two Environments (e.g. parent-child). Aggregate root.
/// </summary>
public sealed record TrustRelationship(
    TrustRelationshipId Id,
    EnvironmentId LocalEnvironmentId,
    EnvironmentId RemoteEnvironmentId,
    RelationshipType RelationshipType,
    RelationshipState State,
    string Direction,
    IReadOnlyList<GrantedCapability> GrantedCapabilities,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? ExpiresAtUtc);
