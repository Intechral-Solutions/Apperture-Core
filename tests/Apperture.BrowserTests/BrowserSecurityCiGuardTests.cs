using Apperture.BrowserTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Apperture.BrowserTests;

/// <summary>
/// SEC-1B4 / I-2: the browser security suite is a blocking GitHub Actions job. These run in a plain
/// <c>dotnet test Apperture.slnx</c> and are not gated by <c>APPERTURE_BROWSER_TESTS</c>.
/// </summary>
public sealed class BrowserSecurityCiGuardTests
{
    [Fact]
    public void Ci_has_a_blocking_browser_security_job()
    {
        var yaml = Workflow();
        var jobsAt = yaml.IndexOf("\njobs:", StringComparison.Ordinal);
        jobsAt.Should().BeGreaterThan(0);
        var preamble = yaml[..jobsAt];
        preamble.Should().Contain("pull_request:");
        preamble.Should().Contain("branches: [main, master]");

        var jobAt = yaml.IndexOf("\n  browser-security:", StringComparison.Ordinal);
        jobAt.Should().BeGreaterThan(0, "the stable check name is the job id browser-security");

        var slnxJob = yaml[..jobAt];
        slnxJob.Should().Contain("dotnet test Apperture.slnx");
        slnxJob.Should().NotContain("APPERTURE_BROWSER_TESTS", "the ordinary slnx job must keep the suite opt-in");

        var job = yaml[jobAt..];
        var stepsAt = job.IndexOf("\n    steps:", StringComparison.Ordinal);
        stepsAt.Should().BeGreaterThan(0);
        job[..stepsAt].Should().NotContain("if:", "the job must not be skipped on pull_request");
        job.Should().Contain("name: browser-security");
        job.Should().Contain("APPERTURE_BROWSER_TESTS: \"1\"");
        job.Should().Contain("chromium firefox webkit");
        job.Should().Contain("APPERTURE_TEST_SQL_CONNECTION_STRING");
        job.Should().Contain("timeout-minutes: 60");
        job.Should().Contain("upload-artifact");
        job.Should().Contain("if: failure()");
        job.Should().NotContain("continue-on-error");
        job.Should().NotContain("|| true");
    }

    private static string Workflow()
    {
        var root = BrowserSqlConfiguration.FindRepoRoot()
            ?? throw new InvalidOperationException("Repository root (.git) not found.");
        return File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
    }
}
