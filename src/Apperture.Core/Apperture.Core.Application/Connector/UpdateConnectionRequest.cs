namespace Apperture.Core.Application.Connector;

/// <summary>
/// Request to update a connection (partial update).
/// </summary>
/// <param name="SecretValues">Optional plaintext secret field values; stored via <c>ISecretStore</c> and merged into secret refs.</param>
public sealed record UpdateConnectionRequest(
    string? Name = null,
    IReadOnlyDictionary<string, string>? Config = null,
    IReadOnlyDictionary<string, string>? SecretRefs = null,
    IReadOnlyDictionary<string, string>? SecretValues = null);
