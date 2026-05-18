using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Diagnostics;

public sealed class Epic77UatDocumentationTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Uat_checklist_exists_and_covers_milestone_flows()
    {
        var path = Path.Combine(
            RepoRoot,
            "docs",
            "03-operations",
            "epic77-notification-system-uat-checklist.md");
        File.Exists(path).Should().BeTrue();

        var content = File.ReadAllText(path);
        content.Should().Contain("Built-in expected-run-missing flow");
        content.Should().Contain("User-created notification definition flow");
        content.Should().Contain("Subscription flow");
        content.Should().Contain("CanUpdateIntegrations");
        content.Should().Contain("UsePlatformPipeline");
        content.Should().Contain("No retry-now");
    }

    [Fact]
    public void Backlog_documents_known_limitations()
    {
        var path = Path.Combine(
            RepoRoot,
            "docs",
            "03-operations",
            "epic77-notification-system-backlog.md");
        File.Exists(path).Should().BeTrue();

        var content = File.ReadAllText(path);
        content.Should().Contain("log-source");
        content.Should().Contain("digest");
        content.Should().Contain("escalation");
        content.Should().Contain("NotificationUiRoutes");
        content.Should().Contain("production cutover");
    }

    [Fact]
    public void NotificationUiRoutes_exposes_drill_down_paths()
    {
        var path = Path.Combine(
            RepoRoot,
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Shared",
            "NotificationUiRoutes.cs");
        var content = File.ReadAllText(path);
        content.Should().Contain("DefinitionEdit");
        content.Should().Contain("IntegrationCutoverReadiness");
        content.Should().Contain("IntegrationScheduleDiagnostics");
    }
}
