namespace Apperture.Bootstrap;

/// <summary>
/// Provides bootstrap credentials as raw config. DEV ONLY. Must not resolve from operational ISecretStore.
/// Prefer <see cref="IBootstrapCredentialResolver"/> with <see cref="Models.BootstrapCredentialReference"/> for production.
/// </summary>
public interface ICredentialSource
{
    /// <summary>
    /// Attempts to get SQL server, database, user, and password for bootstrap connection.
    /// Returns null if this source cannot provide credentials.
    /// Note: Returns plaintext config; use only in Development. Not currently wired in DI.
    /// </summary>
    BootstrapSqlConfig? TryGetConfig();
}
