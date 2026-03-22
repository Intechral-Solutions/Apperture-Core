using Apperture.Management.Client;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Caches operator capabilities for permission-aware UI rendering. Epic 34.
/// </summary>
public interface IOperatorCapabilitiesService
{
    Task<OperatorCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class OperatorCapabilitiesService : IOperatorCapabilitiesService
{
    private readonly IManagementClient _client;
    private OperatorCapabilitiesDto? _cached;

    public OperatorCapabilitiesService(IManagementClient client) => _client = client;

    public async Task<OperatorCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
            return _cached;
        _cached = await _client.GetOperatorCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        return _cached;
    }
}
