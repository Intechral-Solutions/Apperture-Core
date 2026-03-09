namespace Apperture.Bootstrap;

/// <summary>
/// Provides bootstrap credentials for SQL connection. Must not resolve from operational ISecretStore.
/// </summary>
public interface ICredentialSource
{
    /// <summary>
    /// Attempts to get SQL server, database, user, and password for bootstrap connection.
    /// Returns null if this source cannot provide credentials.
    /// </summary>
    BootstrapSqlConfig? TryGetConfig();
}
