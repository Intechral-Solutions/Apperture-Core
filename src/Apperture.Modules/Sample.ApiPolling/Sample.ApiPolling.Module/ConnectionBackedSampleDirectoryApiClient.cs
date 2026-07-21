using Apperture.ModuleSdk.Integration.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

/// <summary>
/// Resolves <see cref="ISampleDirectoryApiClient"/> from the integration connection binding when available.
/// Embedded fake fallback is allowed only when explicitly designated (SDK-6D):
/// <see cref="SampleApiPollingOptions.AllowEmbeddedFallback"/> or
/// <c>APPERTURE_SAMPLE_APIPOLLING_ALLOW_EMBEDDED_FALLBACK=true</c>.
/// Module unit tests and <see cref="Apperture.ModuleSdk.Integration.Testing.IntegrationProcedureTestHost"/>
/// typically inject <see cref="EmbeddedSampleDirectoryApiClient"/> directly instead of this adapter.
/// </summary>
public sealed class ConnectionBackedSampleDirectoryApiClient : ISampleDirectoryApiClient
{
    private readonly IServiceProvider _services;
    private readonly EmbeddedSampleDirectoryApiClient _fallback = new();
    private readonly bool _allowEmbeddedFallback;

    public ConnectionBackedSampleDirectoryApiClient(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _allowEmbeddedFallback = ResolveAllowEmbeddedFallback(services);
    }

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

        if (_allowEmbeddedFallback)
            return _fallback;

        throw new InvalidOperationException(
            "Directory API connection binding is required. Embedded fallback is disabled outside designated " +
            "development/testing paths. Configure SampleApiPolling:AllowEmbeddedFallback or set " +
            $"{SampleApiPollingOptions.AllowEmbeddedFallbackEnvironmentVariable}=true for local runs.");
    }

    internal static bool ResolveAllowEmbeddedFallback(IServiceProvider services)
    {
        var env = Environment.GetEnvironmentVariable(SampleApiPollingOptions.AllowEmbeddedFallbackEnvironmentVariable);
        if (string.Equals(env, "true", StringComparison.OrdinalIgnoreCase))
            return true;

        var options = services.GetService<IOptions<SampleApiPollingOptions>>();
        return options?.Value.AllowEmbeddedFallback == true;
    }
}
