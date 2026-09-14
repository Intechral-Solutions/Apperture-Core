using Apperture.AppHost.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Apperture.AppHost.Diagnostics.Tests;

/// <summary>
/// Regression tests for the decision logic behind CTX-3A's SQL dev-port collision incident: Docker left
/// the Aspire SQL container "running" while silently failing to publish host port 14333 (already owned
/// by an unrelated leftover container), and Management Host/Worker connected straight through to that
/// unrelated server. These tests pin the exact `docker` CLI outputs observed during that incident to
/// the health results they must produce, without needing a Docker daemon.
/// </summary>
public sealed class SqlDevPortGuardTests
{
    private const string VolumeName = "apperture-aspire-sql";
    private const int ExpectedPort = 14333;

    // --- ResolveContainerId ---

    [Fact]
    public void ResolveContainerId_returns_the_id_when_exactly_one_container_mounts_the_volume()
    {
        var (containerId, failure) = SqlDevPortGuard.ResolveContainerId(
            psExitCode: 0, psStdOut: "7109d798d353\n", psStdErr: "", VolumeName);

        containerId.Should().Be("7109d798d353");
        failure.Should().BeNull();
    }

    [Fact]
    public void ResolveContainerId_fails_when_no_container_mounts_the_volume()
    {
        var (containerId, failure) = SqlDevPortGuard.ResolveContainerId(
            psExitCode: 0, psStdOut: "", psStdErr: "", VolumeName);

        containerId.Should().BeNull();
        failure!.Value.Status.Should().Be(HealthStatus.Unhealthy);
        failure.Value.Description.Should().Contain(VolumeName);
    }

    [Fact]
    public void ResolveContainerId_fails_when_multiple_containers_mount_the_volume()
    {
        var (containerId, failure) = SqlDevPortGuard.ResolveContainerId(
            psExitCode: 0, psStdOut: "abc123\ndef456\n", psStdErr: "", VolumeName);

        containerId.Should().BeNull();
        failure!.Value.Status.Should().Be(HealthStatus.Unhealthy);
        failure.Value.Description.Should().Contain("Multiple running containers");
    }

    [Fact]
    public void ResolveContainerId_fails_when_docker_ps_itself_errors()
    {
        var (containerId, failure) = SqlDevPortGuard.ResolveContainerId(
            psExitCode: 1, psStdOut: "", psStdErr: "Cannot connect to the Docker daemon", VolumeName);

        containerId.Should().BeNull();
        failure!.Value.Status.Should().Be(HealthStatus.Unhealthy);
        failure.Value.Description.Should().Contain("Cannot connect to the Docker daemon");
    }

    // --- EvaluatePortPublished ---

    [Fact]
    public void EvaluatePortPublished_is_healthy_when_docker_port_reports_the_expected_binding()
    {
        // Observed: `docker port <id> 1433/tcp` -> "127.0.0.1:14333"
        var result = SqlDevPortGuard.EvaluatePortPublished(
            containerId: "sql-9dfe7faf",
            dockerPortExitCode: 0,
            dockerPortStdOut: "127.0.0.1:14333\n",
            dockerPortStdErr: "",
            ExpectedPort);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void EvaluatePortPublished_is_unhealthy_when_docker_port_reports_no_publish()
    {
        // Observed during the incident: Docker left the container "running" but never actually bound
        // 127.0.0.1:14333 for it (a different, pre-existing container already held that port), so
        // `docker port <id> 1433/tcp` exits non-zero with no host binding.
        var result = SqlDevPortGuard.EvaluatePortPublished(
            containerId: "sql-9dfe7faf",
            dockerPortExitCode: 1,
            dockerPortStdOut: "",
            dockerPortStdErr: "no public port '1433/tcp' published for sql-9dfe7faf",
            ExpectedPort);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("did NOT publish host port 14333");
    }

    [Fact]
    public void EvaluatePortPublished_is_unhealthy_when_the_bound_port_does_not_match_the_expected_one()
    {
        // Guards identity, not just "some" publish: a binding to the wrong port is just as wrong as no
        // binding at all, since dependents were told to expect ExpectedPort specifically.
        var result = SqlDevPortGuard.EvaluatePortPublished(
            containerId: "sql-9dfe7faf",
            dockerPortExitCode: 0,
            dockerPortStdOut: "127.0.0.1:59999\n",
            dockerPortStdErr: "",
            ExpectedPort);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
