namespace Apperture.Bootstrap;

/// <summary>
/// Provides the bootstrap SQL connection profile for platform persistence.
/// Credentials must not be sourced from the operational secret store.
/// </summary>
public interface IBootstrapConnectionProfile
{
    /// <summary>
    /// Gets the validated SQL Server connection string for platform persistence.
    /// </summary>
    string ConnectionString { get; }

    /// <summary>
    /// Validates that the connection can be established. Call before commit/startup.
    /// </summary>
    Task ValidateAsync(CancellationToken cancellationToken = default);
}
