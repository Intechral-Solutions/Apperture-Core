using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap;

/// <summary>
/// Manages the SQL connection bootstrap profile. Builds IBootstrapConnectionProfile.
/// </summary>
public interface ISqlConnectionBootstrapProfileManager
{
    /// <summary>Gets the current SQL connection profile.</summary>
    Task<SqlConnectionBootstrapProfile?> GetProfileAsync(CancellationToken cancellationToken = default);

    /// <summary>Updates the SQL connection profile.</summary>
    Task UpdateProfileAsync(SqlConnectionBootstrapProfile profile, CancellationToken cancellationToken = default);
}
