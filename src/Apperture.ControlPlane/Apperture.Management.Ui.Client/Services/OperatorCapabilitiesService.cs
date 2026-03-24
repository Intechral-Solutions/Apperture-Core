using Apperture.Management.Client;
using Microsoft.Extensions.Logging;

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
    private static readonly OperatorCapabilitiesDto DenyAll =
        new(false, false, false, false, false, false, false, false, false, false, false, false, false, false, false);

    private readonly IManagementClient _client;
    private readonly ILogger<OperatorCapabilitiesService> _logger;
    private OperatorCapabilitiesDto? _cached;

    public OperatorCapabilitiesService(IManagementClient client, ILogger<OperatorCapabilitiesService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<OperatorCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
            return _cached;
        try
        {
            var dto = await _client.GetOperatorCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            // Do not cache an all-deny payload: wrong ApiBaseUrl or bad JSON once left sessions stuck on deny-all.
            if (IsDenyAllCapabilities(dto))
            {
                _logger.LogWarning(
                    "Operator capabilities are all false; not caching. If this persists, check ApiBaseUrl (WASM must reach the management API, e.g. http://localhost:9000/api in Development).");
                return dto;
            }

            _cached = dto;
            return _cached;
        }
        catch (Exception ex)
        {
            // Avoid unhandled renderer exceptions when the API errors; do not cache failures so a later
            // authenticated request (e.g. after login, or WASM after prerender) can succeed.
            _logger.LogWarning(ex, "Operator capabilities request failed; using deny-all until next successful fetch.");
            return DenyAll;
        }
    }

    private static bool IsDenyAllCapabilities(OperatorCapabilitiesDto d) =>
        !d.CanManageSchedules && !d.CanManageRecovery && !d.CanViewAudit && !d.CanViewRuntimeHealth && !d.CanManageModules
        && !d.CanViewGovernance && !d.CanAssignPermissions && !d.CanAssignRoles && !d.CanManageAuthUiPolicy
        && !d.CanViewAuthConfiguration && !d.CanManageEntraTenants && !d.CanViewConnections
        && !d.CanViewIntegrations && !d.CanManageIntegrations && !d.CanViewConnectorDefinitions;
}
