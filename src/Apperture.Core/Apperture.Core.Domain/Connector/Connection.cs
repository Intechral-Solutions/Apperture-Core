using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Governed stored resource: configuration and secret references for resolving a live connector.
/// Not the live client. Resolves to ephemeral client via IConnectionResolver.
/// </summary>
public sealed record Connection(
    ConnectionId Id,
    ConnectorDefinitionId ConnectorDefinitionId,
    string Name,
    IReadOnlyDictionary<string, string> Config,
    IReadOnlyDictionary<string, string> SecretRefs,
    string? ScopeKey,
    string? OwnerId,
    ConnectionState State = ConnectionState.Active) : IConnectionResource
{
    /// <inheritdoc />
    public IGovernedResourceIdentity GetIdentity() =>
        new Governance.GovernedResourceIdentity(
            ResourceTypeConstants.Connection,
            Id.Value,
            ScopeKey ?? $"instance",
            OwnerId);
}
