using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Governed stored resource: configuration and secret references for resolving a live connector.
/// Not the live client. Resolves to ephemeral client via IConnectionResolver.
/// </summary>
/// <param name="ScopeKey">
/// <b>Authorization</b> scope of this Connection (see <see cref="ScopeKeyConstants"/>). This is not a
/// product context and is unrelated to <see cref="ResourceContextId"/>. Do not conflate the two.
/// </param>
public sealed record Connection(
    ConnectionId Id,
    ConnectorDefinitionId ConnectorDefinitionId,
    string Name,
    IReadOnlyDictionary<string, string> Config,
    IReadOnlyDictionary<string, string> SecretRefs,
    string? ScopeKey,
    string? OwnerId,
    ConnectionState State = ConnectionState.Active,
    ConnectionVerificationStatus LastVerificationStatus = ConnectionVerificationStatus.NeverTested,
    DateTimeOffset? LastVerificationAtUtc = null,
    string? LastVerificationSummary = null) : IConnectionResource
{
    /// <summary>
    /// The ResourceContext that owns this Connection. Required in storage; defaults to the reserved
    /// Shared context.
    /// </summary>
    /// <remarks>
    /// Connections stay reusable and are not Integration-owned. An Integration may bind a Connection
    /// when the Connection is in the Integration's own ResourceContext or in Shared. Secret references
    /// and SecretStore behavior are unaffected by ownership: moving contexts never moves or copies
    /// secret material.
    /// </remarks>
    public ResourceContextId ResourceContextId { get; init; } = ResourceContextIds.Shared;

    /// <inheritdoc />
    public IGovernedResourceIdentity GetIdentity() =>
        new Governance.GovernedResourceIdentity(
            ResourceTypeConstants.Connection,
            Id.Value,
            ScopeKey ?? $"instance",
            OwnerId);
}
