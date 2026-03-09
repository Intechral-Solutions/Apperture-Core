using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap.Stubs;

/// <summary>
/// Stub implementation. Throws NotImplementedException. To be replaced by real implementation.
/// </summary>
public sealed class StubSqlConnectionBootstrapProfileManager : ISqlConnectionBootstrapProfileManager
{
    public Task<SqlConnectionBootstrapProfile?> GetProfileAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("ISqlConnectionBootstrapProfileManager is not implemented.");

    public Task UpdateProfileAsync(SqlConnectionBootstrapProfile profile, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("ISqlConnectionBootstrapProfileManager is not implemented.");
}
