using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace Apperture.ControlPlane.Security;

public sealed class DevWorkerRuntimeRecoveryService : IDevWorkerRuntimeRecoveryService
{
    private readonly IHostEnvironment _hostEnvironment;

    public DevWorkerRuntimeRecoveryService(IHostEnvironment hostEnvironment)
    {
        _hostEnvironment = hostEnvironment;
    }

    public async ValueTask<DevWorkerRuntimeRecoveryResult> StartAsync(bool restart, CancellationToken cancellationToken = default)
    {
        if (!_hostEnvironment.IsDevelopment())
            return new DevWorkerRuntimeRecoveryResult(false, "Dev worker recovery is only available in Development.", null, null, null);

        var repoRoot = ResolveRepoRoot();
        var script = Path.Combine(repoRoot, "scripts", "dev", restart ? "restart-worker.ps1" : "start-worker.ps1");
        if (!File.Exists(script))
            return new DevWorkerRuntimeRecoveryResult(false, $"Script not found: {script}", null, null, null);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Headless",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
            return new DevWorkerRuntimeRecoveryResult(false, "Failed to start recovery script process.", null, null, null);

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        var succeeded = process.ExitCode == 0;
        return new DevWorkerRuntimeRecoveryResult(
            succeeded,
            succeeded ? "Worker runtime start request completed." : "Worker runtime start request failed.",
            process.ExitCode,
            string.IsNullOrWhiteSpace(output) ? null : output.Trim(),
            string.IsNullOrWhiteSpace(error) ? null : error.Trim());
    }

    public async ValueTask<DevWorkerRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var repoRoot = ResolveRepoRoot();
        var path = Path.Combine(repoRoot, "artifacts", "devlogs", "worker-runtime-state.json");
        if (!File.Exists(path))
            return new DevWorkerRuntimeStatus("Unknown", "No worker runtime state file found.", null, null, null, null);

        await using var stream = File.OpenRead(path);
        var dto = await JsonSerializer.DeserializeAsync<StateDto>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (dto is null)
            return new DevWorkerRuntimeStatus("Unknown", "Failed to parse worker runtime state.", null, null, null, null);

        return new DevWorkerRuntimeStatus(
            dto.status ?? "Unknown",
            dto.message ?? "",
            dto.pid,
            TryParse(dto.lastStartAttemptUtc),
            TryParse(dto.lastUpdatedUtc),
            dto.logPath);
    }

    private static DateTimeOffset? TryParse(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static string ResolveRepoRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("APPERTURE_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();

        var cwd = Directory.GetCurrentDirectory();
        var dir = new DirectoryInfo(cwd);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Apperture.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return cwd;
    }

    private sealed record StateDto(
        string? status,
        string? message,
        int? pid,
        string? lastStartAttemptUtc,
        string? lastUpdatedUtc,
        string? logPath);
}
