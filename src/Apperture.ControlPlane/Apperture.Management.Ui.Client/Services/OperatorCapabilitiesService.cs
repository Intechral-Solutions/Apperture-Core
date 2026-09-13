using Apperture.Management.Client;
using Microsoft.Extensions.Logging;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Caches operator capabilities for permission-aware UI rendering. Epic 34.
/// </summary>
public interface IOperatorCapabilitiesService
{
    /// <summary>Fired after <see cref="RefreshAsync"/> or <see cref="InvalidateCache"/>.</summary>
    event Action? Changed;

    Task<OperatorCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>Clears the in-memory cache so the next fetch hits the API.</summary>
    void InvalidateCache();

    /// <summary>Forces a new capabilities fetch (with retry) and updates the cache when successful.</summary>
    Task<OperatorCapabilitiesDto> RefreshAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class OperatorCapabilitiesService : IOperatorCapabilitiesService
{
    private static readonly OperatorCapabilitiesDto DenyAll =
        new(false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false);

    private static readonly int[] RetryDelaysMs = [50, 150];

    private readonly IManagementClient _client;
    private readonly ILogger<OperatorCapabilitiesService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private OperatorCapabilitiesDto? _cached;

    public OperatorCapabilitiesService(IManagementClient client, ILogger<OperatorCapabilitiesService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public event Action? Changed;

    public async Task<OperatorCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
            return _cached;

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null)
                return _cached;

            return await FetchAndMaybeCacheAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public void InvalidateCache()
    {
        _cached = null;
        Changed?.Invoke();
    }

    public async Task<OperatorCapabilitiesDto> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cached = null;
            var dto = await FetchAndMaybeCacheAsync(cancellationToken).ConfigureAwait(false);
            Changed?.Invoke();
            return dto;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<OperatorCapabilitiesDto> FetchAndMaybeCacheAsync(CancellationToken cancellationToken)
    {
        var dto = await FetchWithRetryAsync(cancellationToken).ConfigureAwait(false);
        if (!IsDenyAllCapabilities(dto))
            _cached = dto;
        return dto;
    }

    private async Task<OperatorCapabilitiesDto> FetchWithRetryAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var dto = await _client.GetOperatorCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
                if (!IsDenyAllCapabilities(dto))
                    return dto;

                if (attempt < maxAttempts)
                {
                    _logger.LogDebug(
                        "Operator capabilities are all false on attempt {Attempt}/{MaxAttempts}; retrying.",
                        attempt,
                        maxAttempts);
                    await Task.Delay(RetryDelaysMs[attempt - 1], cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _logger.LogWarning(
                    "Operator capabilities are all false after {MaxAttempts} attempts; not caching. If this persists, check ApiBaseUrl (WASM must reach the management API, e.g. http://localhost:9000/api in Development).",
                    maxAttempts);
                return dto;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Operator capabilities request failed on attempt {Attempt}/{MaxAttempts}; retrying.",
                    attempt,
                    maxAttempts);
                await Task.Delay(RetryDelaysMs[attempt - 1], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Operator capabilities request failed after {MaxAttempts} attempts; using deny-all until next successful fetch.",
                    maxAttempts);
                return DenyAll;
            }
        }

        return DenyAll;
    }

    private static bool IsDenyAllCapabilities(OperatorCapabilitiesDto d) =>
        !d.CanManageSchedules && !d.CanManageRecovery && !d.CanViewAudit && !d.CanViewRuntimeHealth && !d.CanManageModules
        && !d.CanViewGovernance && !d.CanAssignPermissions && !d.CanAssignRoles && !d.CanManageAuthUiPolicy
        && !d.CanViewAuthConfiguration && !d.CanManageEntraTenants && !d.CanViewConnections
        && !d.CanViewIntegrations && !d.CanManageIntegrations && !d.CanViewConnectorDefinitions
        && !d.CanCreateConnections && !d.CanUpdateConnections && !d.CanRetireConnections
        && !d.CanViewManagedContacts && !d.CanManageManagedContacts && !d.CanImportManagedContacts
        && !d.CanViewNotificationDefinitions && !d.CanManageNotificationDefinitions && !d.CanUpdateIntegrations
        && !d.CanViewPlatformSettings && !d.CanManagePlatformSettings && !d.CanManageUsers;
}
