using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Connector;

/// <summary>
/// Request to create a connection.
/// </summary>
/// <param name="SecretValues">Optional plaintext secret field values; stored via <c>ISecretStore</c> and converted to GUID refs (not persisted as raw values).</param>
public sealed record CreateConnectionRequest(
    ConnectorDefinitionId ConnectorDefinitionId,
    string Name,
    IReadOnlyDictionary<string, string> Config,
    IReadOnlyDictionary<string, string> SecretRefs,
    string? ScopeKey = null,
    string? OwnerId = null,
    IReadOnlyDictionary<string, string>? SecretValues = null);
