namespace Apperture.ModuleSdk.Integration.Configuration;

/// <summary>
/// Runtime access to bound connection capabilities for the current integration.
/// Platform adapters own binding resolution, secret material, client construction, and lifecycle.
/// </summary>
public interface IIntegrationConnectionAccessor
{
    ValueTask<T> GetRequiredAsync<T>(string connectionKey, CancellationToken cancellationToken = default)
        where T : class;

    ValueTask<T?> TryGetAsync<T>(string connectionKey, CancellationToken cancellationToken = default)
        where T : class;

    ValueTask<bool> IsBoundAsync(string connectionKey, CancellationToken cancellationToken = default);
}
