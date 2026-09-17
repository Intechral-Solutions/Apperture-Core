using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests.Integrations;

/// <summary>
/// Razor source-text assertions, matching this suite's established convention (see
/// NotificationDefinitionDeleteUiSourceTests and IntegrationAttentionRoutingClearUiSourceTests).
/// There is no bUnit in this repository; component rendering is not available, so the contract is
/// pinned at the markup level.
/// </summary>
public sealed class IntegrationDeleteBlockerUiSourceTests
{
    private const string Folder = "Pages/Integrations";
    private const string TargetFile = "IntegrationDetail.razor";

    [Fact]
    public void Stale_cascade_copy_is_no_longer_present()
    {
        var content = ReadRazor(Folder, TargetFile);

        content.Should().NotContain(
            "Bindings, schedules, and settings that belong to this integration are removed with it.",
            "post-Plan-6a, Bindings and Schedules are (and always were) delete blockers, not things " +
            "cascaded/removed with the integration");
    }

    [Fact]
    public void Details_fragment_names_all_three_new_blockers_as_must_clear_first_not_cascaded()
    {
        var content = ReadRazor(Folder, TargetFile);
        var details = ExtractDetailsFragment(content);

        // Scoped to the ConfirmActionDialog's <Details> fragment specifically, not just anywhere in
        // this 900+ line file, so this can't false-positive against unrelated copy elsewhere on the
        // page (e.g. the Related diagnostics section already links to schedule diagnostics for other
        // reasons).
        details.Should().Contain("notification definitions");
        details.Should().Contain("notification subscriptions");
        details.Should().Contain("attention-routing overrides");
        details.Should().Contain("must be removed first",
            "the new blockers must be described as things that block delete, not as cascaded/removed automatically");

        // Settings are the one thing that actually IS cascaded -- the copy should distinguish that.
        details.Should().Contain("settings", "Integration-scoped settings are the one thing that genuinely cascades automatically");
        details.Should().Contain("automatically");
    }

    [Fact]
    public void Details_fragment_does_not_speculate_about_who_can_clear_the_subscription_blocker()
    {
        var content = ReadRazor(Folder, TargetFile);
        var details = ExtractDetailsFragment(content);

        // The existing NotificationSubscription removal surface already has its own correct
        // authorization; this dialog must not add new copy guessing at who can act.
        details.Should().NotContain("platform administrator");
        details.Should().NotContain("admin");
    }

    [Fact]
    public void Details_fragment_links_to_the_notifications_and_schedule_diagnostics_remediation_pages()
    {
        var content = ReadRazor(Folder, TargetFile);
        var details = ExtractDetailsFragment(content);

        // Built from this page's existing IntegrationId route parameter via the same
        // IntegrationUiRoutes helpers already used elsewhere on this page (e.g. the "Related
        // diagnostics" section below), not hand-rolled route strings.
        details.Should().Contain("IntegrationUiRoutes.Notifications(IntegrationId)");
        details.Should().Contain("IntegrationUiRoutes.ScheduleDiagnostics(IntegrationId)");
    }

    /// <summary>
    /// Extracts the ConfirmActionDialog's &lt;Details&gt;...&lt;/Details&gt; fragment so assertions
    /// about its copy can't false-positive against unrelated markup elsewhere in this large page.
    /// </summary>
    private static string ExtractDetailsFragment(string content)
    {
        var openTag = "<Details>";
        var startIndex = content.IndexOf(openTag, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, "expected to find the ConfirmActionDialog's <Details> fragment");

        var closeTag = "</Details>";
        var endIndex = content.IndexOf(closeTag, startIndex, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThanOrEqualTo(0, "expected a matching closing </Details> tag");

        return content[(startIndex + openTag.Length)..endIndex];
    }

    private static string ReadRazor(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var path = Path.Combine(dir!.FullName, "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client",
            Path.Combine(folder.Split('/')), file);
        File.Exists(path).Should().BeTrue($"expected {path} to exist");
        return System.IO.File.ReadAllText(path);
    }
}
