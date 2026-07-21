using Apperture.ModuleSdk.Integration.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

/// <summary>
/// Resolves <see cref="ISampleDirectoryApiClient"/> from the integration connection binding when available;
/// falls back to the embedded fake client for local fixtures and tests without a binding.
/// </summary>
public sealed class ConnectionBackedSampleDirectoryApiClient : ISampleDirectoryApiClient
{
    private readonly IServiceProvider _services;
    private readonly EmbeddedSampleDirectoryApiClient _fallback = new();

    public ConnectionBackedSampleDirectoryApiClient(IServiceProvider services) =>
        _services = services ?? throw new ArgumentNullException(nameof(services));

    public IReadOnlyList<string> ListSnapshotIds() =>
        ResolveClientAsync(CancellationToken.None).GetAwaiter().GetResult().ListSnapshotIds();

    public async Task<DirectorySnapshotPage> GetSnapshotAsync(
        string snapshotId,
        ApiPollQuery query,
        CancellationToken cancellationToken = default)
    {
        var client = await ResolveClientAsync(cancellationToken).ConfigureAwait(false);
        return await client.GetSnapshotAsync(snapshotId, query, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ISampleDirectoryApiClient> ResolveClientAsync(CancellationToken cancellationToken)
    {
        var accessor = _services.GetService(typeof(IIntegrationConnectionAccessor)) as IIntegrationConnectionAccessor;
        if (accessor is not null)
        {
            var bound = await accessor.TryGetAsync<ISampleDirectoryApiClient>(
                    ApiPollingConnectionKeys.DirectoryApi,
                    cancellationToken)
                .ConfigureAwait(false);
            if (bound is not null)
                return bound;
        }

        return _fallback;
    }
}
