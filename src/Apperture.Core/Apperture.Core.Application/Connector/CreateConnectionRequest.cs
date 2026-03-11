using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Connector;

/// <summary>
/// Request to create a connection.
/// </summary>
public sealed record CreateConnectionRequest(
    ConnectorDefinitionId ConnectorDefinitionId,
    string Name,
    IReadOnlyDictionary<string, string> Config,
    IReadOnlyDictionary<string, string> SecretRefs,
    string? ScopeKey = null,
    string? OwnerId = null);
