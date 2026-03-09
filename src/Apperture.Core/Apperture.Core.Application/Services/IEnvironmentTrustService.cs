using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Trust;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Manages trust relationships between environments (create, approve, suspend, revoke, list). GrantedCapability scope.
/// </summary>
public interface IEnvironmentTrustService
{
    /// <summary>
    /// Creates a trust relationship request.
    /// </summary>
    ValueTask<TrustRelationship> CreateRequestAsync(
        EnvironmentId localEnvironmentId,
        EnvironmentId remoteEnvironmentId,
        RelationshipType relationshipType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a pending trust relationship.
    /// </summary>
    ValueTask ApproveAsync(TrustRelationshipId relationshipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Suspends an active trust relationship.
    /// </summary>
    ValueTask SuspendAsync(TrustRelationshipId relationshipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a trust relationship.
    /// </summary>
    ValueTask RevokeAsync(TrustRelationshipId relationshipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets trust relationships for an environment.
    /// </summary>
    ValueTask<IReadOnlyList<TrustRelationship>> GetRelationshipsAsync(EnvironmentId environmentId, CancellationToken cancellationToken = default);
}
