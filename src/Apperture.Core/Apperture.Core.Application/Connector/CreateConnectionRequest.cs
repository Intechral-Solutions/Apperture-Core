using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Core.Application.Connector;

/// <summary>
/// Request to create a connection.
/// </summary>
/// <param name="ScopeKey">Authorization scope. Unrelated to <paramref name="ResourceContextId"/>.</param>
/// <param name="SecretValues">Optional plaintext secret field values; stored via <c>ISecretStore</c> and converted to GUID refs (not persisted as raw values).</param>
/// <param name="ResourceContextId">
/// Owning ResourceContext. Optional: when absent the Connection is owned by the reserved Shared context,
/// so existing clients that omit it keep working. A supplied context must exist and be Active. Ownership
/// never affects secret references or SecretStore behavior.
/// </param>
public sealed record CreateConnectionRequest(
    ConnectorDefinitionId ConnectorDefinitionId,
    string Name,
    IReadOnlyDictionary<string, string> Config,
    IReadOnlyDictionary<string, string> SecretRefs,
    string? ScopeKey = null,
    string? OwnerId = null,
    IReadOnlyDictionary<string, string>? SecretValues = null,
    ResourceContextId? ResourceContextId = null);
