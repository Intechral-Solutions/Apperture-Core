using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Apperture.AppHost.Diagnostics;

/// <summary>
/// Verifies that the Aspire-managed dev SQL container actually published its configured host port on
/// Docker's side, instead of trusting Aspire's injected connection string blindly.
/// </summary>
/// <remarks>
/// Guards against a failure mode observed in practice: Docker can leave a container "running" while
/// silently failing to publish a requested host port because another process/container already owns
/// it. Aspire still hands out the configured port in <c>DefaultConnection</c> regardless, so
/// Management Host / Worker Host connect straight through to whatever unrelated SQL Server already
/// occupies that port - no exception, no obvious symptom, just the wrong database. Identifying "our"
/// container by the persistent data volume it mounts (rather than its randomly-suffixed name) and
/// checking Docker's own port-publish state closes that gap: at most one container can ever hold a
/// real listen binding for a given host port, so a positive result here is proof of identity, not just
/// reachability.
///
/// Decision logic is split into pure, synchronous methods (<see cref="ResolveContainerId"/>,
/// <see cref="EvaluatePortPublished"/>) so it can be unit tested without a Docker daemon; only
/// <see cref="VerifyHostPortPublishedAsync"/> itself shells out to the `docker` CLI.
/// </remarks>
public static class SqlDevPortGuard
{
    public static async Task<HealthCheckResult> VerifyHostPortPublishedAsync(
        string dataVolumeName,
        int expectedHostPort,
        CancellationToken cancellationToken)
    {
        var (psExitCode, psStdOut, psStdErr) = await RunDockerAsync(
            ["ps", "--filter", $"volume={dataVolumeName}", "--filter", "status=running", "--format", "{{.ID}}"],
            cancellationToken);

        var (containerId, failure) = ResolveContainerId(psExitCode, psStdOut, psStdErr, dataVolumeName);
        if (failure is not null)
        {
            return failure.Value;
        }

        var (portExitCode, portStdOut, portStdErr) = await RunDockerAsync(
            ["port", containerId!, "1433/tcp"],
            cancellationToken);

        return EvaluatePortPublished(containerId!, portExitCode, portStdOut, portStdErr, expectedHostPort);
    }

    public static (string? ContainerId, HealthCheckResult? Failure) ResolveContainerId(
        int psExitCode,
        string psStdOut,
        string psStdErr,
        string volumeName)
    {
        if (psExitCode != 0)
        {
            return (null, HealthCheckResult.Unhealthy(
                $"Could not query Docker to locate the SQL container mounting volume '{volumeName}': " +
                $"'docker ps' exited {psExitCode}: {psStdErr.Trim()}"));
        }

        var trimmed = psStdOut.Trim();
        if (trimmed.Length == 0)
        {
            return (null, HealthCheckResult.Unhealthy(
                $"No running container is mounting the persistent volume '{volumeName}' yet."));
        }

        if (trimmed.Contains('\n'))
        {
            return (null, HealthCheckResult.Unhealthy(
                $"Multiple running containers mount volume '{volumeName}' - ambiguous state, investigate with " +
                $"'docker ps --filter volume={volumeName}'."));
        }

        return (trimmed, null);
    }

    public static HealthCheckResult EvaluatePortPublished(
        string containerId,
        int dockerPortExitCode,
        string dockerPortStdOut,
        string dockerPortStdErr,
        int expectedHostPort)
    {
        if (dockerPortExitCode != 0 || !dockerPortStdOut.Contains($":{expectedHostPort}", StringComparison.Ordinal))
        {
            return HealthCheckResult.Unhealthy(
                $"SQL container {containerId} is running but did NOT publish host port {expectedHostPort} " +
                $"(docker port output: '{dockerPortStdOut.Trim()}{dockerPortStdErr.Trim()}'). Another process or " +
                $"container is almost certainly already bound to 127.0.0.1:{expectedHostPort} - Management " +
                "Host/Worker would otherwise silently connect to that unrelated server instead of the real dev " +
                "database. Free the port (see CONTRIBUTING.md: Aspire dev SQL owns 14333) and restart AppHost.");
        }

        return HealthCheckResult.Healthy(
            $"SQL container {containerId} is publishing 127.0.0.1:{expectedHostPort} as expected.");
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunDockerAsync(
        string[] arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start 'docker' process.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await stdOutTask, await stdErrTask);
    }
}
