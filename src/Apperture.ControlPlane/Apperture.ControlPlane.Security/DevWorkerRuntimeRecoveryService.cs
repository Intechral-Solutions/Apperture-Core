using Apperture.Core.Application.RuntimeHealth;
using Microsoft.Extensions.Hosting;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// SWM-9a: Epic72B worker script lifecycle is quarantined.
/// Does not spawn processes or imply supported start/stop/restart.
/// </summary>
public sealed class DevWorkerRuntimeRecoveryService : IDevWorkerRuntimeRecoveryService
{
    private readonly IHostEnvironment _hostEnvironment;

    public DevWorkerRuntimeRecoveryService(IHostEnvironment hostEnvironment)
    {
        _hostEnvironment = hostEnvironment;
    }

    public ValueTask<DevWorkerRuntimeRecoveryResult> StartAsync(bool restart, CancellationToken cancellationToken = default)
    {
        // Prefer clear quarantine over shelling to missing / Aspire-unsafe scripts.
        _ = restart;
        _ = _hostEnvironment;
        return ValueTask.FromResult(new DevWorkerRuntimeRecoveryResult(
            Succeeded: false,
            Message: WorkerLifecycleQuarantine.StartDeniedMessage,
            ExitCode: null,
            Output: null,
            Error: WorkerLifecycleQuarantine.ErrorCode));
    }

    public ValueTask<DevWorkerRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new DevWorkerRuntimeStatus(
            Status: WorkerLifecycleQuarantine.Status,
            Message: WorkerLifecycleQuarantine.StatusMessage,
            Pid: null,
            LastStartAttemptUtc: null,
            LastUpdatedUtc: DateTimeOffset.UtcNow,
            LogPath: null));
    }
}
