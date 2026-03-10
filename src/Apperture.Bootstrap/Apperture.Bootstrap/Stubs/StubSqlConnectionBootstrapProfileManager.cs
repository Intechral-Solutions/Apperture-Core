using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap.Stubs;

/// <summary>
/// Stub: not for production. GetProfileAsync returns null; UpdateProfileAsync throws. Replace with a real implementation before using bootstrap mutation (profile update).
/// </summary>
public sealed class StubSqlConnectionBootstrapProfileManager : ISqlConnectionBootstrapProfileManager
{
    /// <inheritdoc />
    public Task<SqlConnectionBootstrapProfile?> GetProfileAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<SqlConnectionBootstrapProfile?>(null);

    /// <inheritdoc />
    public Task UpdateProfileAsync(SqlConnectionBootstrapProfile profile, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Bootstrap profile persistence not implemented. Stub cannot save profile. Configure a real ISqlConnectionBootstrapProfileManager.");
}
