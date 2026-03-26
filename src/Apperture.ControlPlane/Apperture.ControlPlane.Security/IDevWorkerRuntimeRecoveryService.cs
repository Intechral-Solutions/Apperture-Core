namespace Apperture.ControlPlane.Security;

public interface IDevWorkerRuntimeRecoveryService
{
    ValueTask<DevWorkerRuntimeRecoveryResult> StartAsync(bool restart, CancellationToken cancellationToken = default);
    ValueTask<DevWorkerRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

public sealed record DevWorkerRuntimeRecoveryResult(
    bool Succeeded,
    string Message,
    int? ExitCode,
    string? Output,
    string? Error);

public sealed record DevWorkerRuntimeStatus(
    string Status,
    string Message,
    int? Pid,
    DateTimeOffset? LastStartAttemptUtc,
    DateTimeOffset? LastUpdatedUtc,
    string? LogPath);
