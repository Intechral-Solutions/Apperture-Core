using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>SWM-12: worker hosting script/docs hygiene — no supported start-worker scripts; quarantine UI aligned.</summary>
public sealed class Swm12WorkerHostingHygieneTests
{
    [Fact]
    public void Missing_lifecycle_scripts_are_not_present_in_repo()
    {
        File.Exists(RepoPath("scripts/dev/start-worker.ps1")).Should().BeFalse();
        File.Exists(RepoPath("scripts/dev/restart-worker.ps1")).Should().BeFalse();
    }

    [Theory]
    [InlineData("docs/03-operations/Worker-Hosting-Guidance-v1.md")]
    [InlineData("docs/03-operations/Worker-Runtime-Dev-Recovery.md")]
    [InlineData("CONTRIBUTING.md")]
    [InlineData("scripts/README.md")]
    [InlineData("deploy/README.md")]
    public void Supported_ops_docs_do_not_advertise_missing_scripts_as_commands_to_run(string relativePath)
    {
        var content = File.ReadAllText(RepoPath(relativePath));
        // Disallow shelling to missing scripts as a runnable path; historical mentions must stay non-imperative.
        content.Should().NotMatchRegex(@"(?im)^\s*(\.\\|\./)?scripts[/\\]dev[/\\]start-worker\.ps1\b");
        content.Should().NotMatchRegex(@"(?i)Manage\.ps1[^\n]{0,40}start-worker");
        // Mention WorkerHost or worker host ownership (docs vary phrasing).
        (content.Contains("WorkerHost", StringComparison.Ordinal)
         || content.Contains("worker host", StringComparison.OrdinalIgnoreCase)
         || content.Contains("Worker runtime", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("ops docs should mention worker hosting ownership clearly");
    }

    [Fact]
    public void Worker_hosting_guidance_states_quarantine_and_preferred_hosts()
    {
        var content = File.ReadAllText(RepoPath("docs/03-operations/Worker-Hosting-Guidance-v1.md"));
        content.Should().Contain("410 Gone");
        content.Should().Contain("AppHost");
        content.Should().Contain("Manage.ps1");
        content.Should().Contain("does **not** start, stop, or restart WorkerHost");
        content.Should().Contain("restored as supported Management actions");
    }

    [Fact]
    public void Epic72B_adr_is_superseded_quarantined()
    {
        var content = File.ReadAllText(RepoPath(
            "docs/02-decisions/Epic72B-Worker-Runtime-Launch-Recovery-and-Workflow-Testability.md"));
        content.Should().Contain("Superseded / Quarantined");
        content.Should().Contain("SWM-12");
        content.Should().NotContain("## Status\r\n\r\nAccepted");
        content.Should().NotContain("## Status\n\nAccepted");
    }

    [Fact]
    public void Developer_and_control_center_point_to_external_lifecycle_not_missing_scripts()
    {
        var developer = File.ReadAllText(RepoPath(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/DeveloperTasks.razor"));
        developer.Should().Contain("410");
        developer.Should().Contain("Aspire");
        developer.Should().Contain("Manage.ps1");
        developer.Should().NotContain("start-worker.ps1");

        var control = File.ReadAllText(RepoPath(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Control/ControlCenter.razor"));
        control.Should().Contain("runtime-worker/start");
        control.Should().Contain("quarantined");
        control.Should().Contain("externally managed");
        control.Should().NotContain("start-worker.ps1");
        control.Should().NotContain("onclick=\"StartWorker");
        control.Should().NotContain("Restart WorkerHost");
    }

    private static string RepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return Path.Combine(dir!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
