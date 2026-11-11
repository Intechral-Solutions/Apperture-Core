using System.Text.RegularExpressions;
using Apperture.BrowserTests.Infrastructure;
using Apperture.SqlTestTargets;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Apperture.BrowserTests;

/// <summary>
/// SEC-1B4 / I-2: the browser security suite is a blocking GitHub Actions job. These run in a plain
/// <c>dotnet test Apperture.slnx</c> and are not gated by <c>APPERTURE_BROWSER_TESTS</c>.
/// </summary>
/// <remarks>
/// The job runs inside Microsoft's Playwright image for .NET, so the browsers, their operating-system libraries and the SDK are
/// part of the image and nothing is installed from the Ubuntu mirrors or the Playwright CDN while the job runs. The contract
/// below keeps that arrangement honest: the image is the one for the pinned Microsoft.Playwright version, the container gets no
/// extra privileges, the SQL server is the job's own service reached through the same SQL target guard as everywhere else, and
/// the suite step still runs the whole suite and fails the job when a test fails.
/// </remarks>
public sealed class BrowserSecurityCiGuardTests
{
    private const string PlaywrightImageRepository = "mcr.microsoft.com/playwright/dotnet";

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
        job.Should().Contain("APPERTURE_TEST_SQL_CONNECTION_STRING");
        job.Should().Contain("timeout-minutes: 60");
        job.Should().Contain("upload-artifact");
        job.Should().Contain("if: failure()");
        job.Should().NotContain("continue-on-error");
        job.Should().NotContain("|| true");
    }

    /// <summary>
    /// The image tag is the Microsoft.Playwright version the project resolves. A package upgrade that leaves the image behind (or the
    /// reverse) would pair the driver with browsers of a different revision, so the two are tied together here.
    /// </summary>
    [Fact]
    public void Browser_job_runs_in_the_playwright_image_pinned_to_the_package_version()
    {
        var job = BrowserSecurityJob();
        var header = job[..job.IndexOf("\n    steps:", StringComparison.Ordinal)];

        var image = Regex.Match(header, "(?m)^    container:\\r?\\n      image: (?<image>\\S+)\\s*$");
        image.Success.Should().BeTrue("the job must run in a container");
        image.Groups["image"].Value.Should().Be($"{PlaywrightImageRepository}:v{PinnedPlaywrightVersion()}-noble",
            "the full Playwright .NET image carries Chromium, Firefox and WebKit, and its tag must be the pinned package version");

        // The container's only extra is shared memory for the three engines. No privileges, host namespaces, devices or mounts.
        var options = Regex.Match(header, "(?m)^      options: (?<options>.+?)\\s*$");
        if (options.Success)
            options.Groups["options"].Value.Should().MatchRegex("^--shm-size=\\d+(m|mb|g|gb)$");
    }

    [Fact]
    public void Browser_job_installs_nothing_from_the_network_while_it_runs()
    {
        var job = BrowserSecurityJob();

        foreach (var installer in new[] { "playwright.ps1", "install-deps", "--with-deps", "apt-get", "ms-playwright" })
            job.Should().NotContain(installer, "the browsers and their libraries come with the pinned image");
    }

    /// <summary>
    /// In a container job the SQL service is reached by its service name on the job network. That string must pass the same
    /// <see cref="SqlTestTargetGuard"/> rules the harness applies before it drops anything, with no opt-in and no published port.
    /// </summary>
    [Fact]
    public void Browser_job_reaches_only_its_own_ephemeral_sql_service_through_the_target_guard()
    {
        var job = BrowserSecurityJob();
        var header = job[..job.IndexOf("\n    steps:", StringComparison.Ordinal)];

        var services = Regex.Matches(header, "(?m)^      (?<name>[A-Za-z][A-Za-z0-9_-]*):\\r?\\n        image: (?<image>\\S+)")
            .Select(m => (Name: m.Groups["name"].Value, Image: m.Groups["image"].Value)).ToArray();
        services.Should().ContainSingle("the job has exactly one service, its own disposable SQL Server");
        services[0].Image.Should().StartWith("mcr.microsoft.com/mssql/server:");
        header.Should().NotContain("ports:", "the service is reached over the job network and needs no published port");

        var template = Regex.Match(Step(job, "Browser security suite"), "format\\('(?<template>[^']+)'");
        template.Success.Should().BeTrue("the suite step builds the connection string from the CI password");
        var connection = template.Groups["template"].Value.Replace("{0}", "ci-password-placeholder", StringComparison.Ordinal);
        var builder = new SqlConnectionStringBuilder(connection);
        builder.DataSource.Should().Be($"{services[0].Name},1433");

        // The real rules: a disposable test server (not the development port) and the harness's own disposable catalog.
        var scratch = new SqlConnectionStringBuilder(BrowserSqlConfiguration.ScratchConnectionStringFor(connection));
        scratch.InitialCatalog.Should().Be(BrowserSqlConfiguration.ScratchDatabaseName);

        job.Should().NotContain(SqlTestTargetGuard.ProtectedTargetOptInVariable, "the protected-target opt-in must never be set in CI");
        job.Should().NotContain(SqlTestTargetGuard.DevelopmentSqlPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Browser_suite_step_runs_the_whole_suite_after_restore_and_build_and_fails_the_job_on_a_failing_test()
    {
        var job = BrowserSecurityJob();

        var restore = job.IndexOf("- name: Restore", StringComparison.Ordinal);
        var build = job.IndexOf("- name: Build", StringComparison.Ordinal);
        var suiteAt = job.IndexOf("- name: Browser security suite", StringComparison.Ordinal);
        restore.Should().BeGreaterThan(0);
        build.Should().BeGreaterThan(restore);
        suiteAt.Should().BeGreaterThan(build);

        var suite = Step(job, "Browser security suite");
        suite.Should().Contain("dotnet test tests/Apperture.BrowserTests/Apperture.BrowserTests.csproj");
        suite.Should().Contain("--no-build");
        suite.Should().Contain("trx;LogFileName=browser-security.trx");
        suite.Should().NotContain("--filter", "the required job runs every browser test");
        suite.Should().NotContain("continue-on-error");
        suite.Should().NotContain("||");
        suite.Should().NotContain("exit 0");
        suite.Should().NotContain("if:");
    }

    [Fact]
    public void The_harness_drives_all_three_engines()
    {
        BrowserEngines.All.Should().BeEquivalentTo([BrowserEngine.Chromium, BrowserEngine.Firefox, BrowserEngine.WebKit]);
    }

    private static string BrowserSecurityJob()
    {
        var yaml = Workflow();
        var jobAt = yaml.IndexOf("\n  browser-security:", StringComparison.Ordinal);
        jobAt.Should().BeGreaterThan(0, "the stable check name is the job id browser-security");
        return yaml[jobAt..];
    }

    /// <summary>One step of the job: from its "- name:" line up to the next step.</summary>
    private static string Step(string job, string name)
    {
        var start = job.IndexOf("- name: " + name, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, $"the job must have a step named '{name}'");
        var next = job.IndexOf("\n      - name:", start + 1, StringComparison.Ordinal);
        return next < 0 ? job[start..] : job[start..next];
    }

    private static string PinnedPlaywrightVersion()
    {
        var props = File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Packages.props"));
        var match = Regex.Match(props, "<PackageVersion Include=\"Microsoft\\.Playwright\" Version=\"(?<version>[^\"]+)\"");
        match.Success.Should().BeTrue("Directory.Packages.props pins Microsoft.Playwright");
        return match.Groups["version"].Value;
    }

    private static string Workflow() =>
        File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));

    private static string RepoRoot() =>
        BrowserSqlConfiguration.FindRepoRoot()
            ?? throw new InvalidOperationException("Repository root (.git) not found.");
}
