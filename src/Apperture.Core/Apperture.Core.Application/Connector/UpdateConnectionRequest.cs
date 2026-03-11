namespace Apperture.Core.Application.Connector;

/// <summary>
/// Request to update a connection (partial update).
/// </summary>
public sealed record UpdateConnectionRequest(
    string? Name = null,
    IReadOnlyDictionary<string, string>? Config = null,
    IReadOnlyDictionary<string, string>? SecretRefs = null);
