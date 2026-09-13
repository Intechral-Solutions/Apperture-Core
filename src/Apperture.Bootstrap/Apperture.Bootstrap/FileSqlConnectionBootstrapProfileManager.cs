using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap;

/// <summary>
/// Persists SQL connection profile changes via <see cref="IBootstrapConfigurationStore"/>.
/// </summary>
public sealed class FileSqlConnectionBootstrapProfileManager : ISqlConnectionBootstrapProfileManager
{
    private readonly IBootstrapConfigurationStore _store;

    public FileSqlConnectionBootstrapProfileManager(IBootstrapConfigurationStore store)
    {
        _store = store;
    }

    public async Task<SqlConnectionBootstrapProfile?> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var doc = await _store.GetAsync(cancellationToken).ConfigureAwait(false);
        return doc?.Profile;
    }

    public async Task UpdateProfileAsync(SqlConnectionBootstrapProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var doc = await _store.GetAsync(cancellationToken).ConfigureAwait(false);
        var updated = doc is null
            ? new BootstrapConfigurationDocument { Profile = profile }
            : doc with { Profile = profile };

        await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
    }
}
