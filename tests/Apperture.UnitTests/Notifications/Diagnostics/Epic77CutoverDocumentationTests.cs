using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Diagnostics;

public sealed class Epic77CutoverDocumentationTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Cutover_runbook_exists_and_covers_enablement_order()
    {
        var path = Path.Combine(
            RepoRoot,
            "docs",
            "03-operations",
            "epic77-expected-run-missing-notification-cutover-runbook.md");
        File.Exists(path).Should().BeTrue($"Expected runbook at {path}");

        var content = File.ReadAllText(path);
        content.Should().Contain("PlatformEvents:Enabled");
        content.Should().Contain("Notifications:Evaluation:Enabled");
        content.Should().Contain("Notifications:Instances:Enabled");
        content.Should().Contain("Notifications:Delivery:Enabled=false");
        content.Should().Contain("Notifications:UsePlatformPipeline");
        content.Should().Contain("Rollback");
        content.Should().Contain("missing_in_platform");
        content.Should().Contain("dual-send");
    }

    [Fact]
    public void Epic77_plan_links_to_runbook()
    {
        var path = Path.Combine(
            RepoRoot,
            "docs",
            "02-decisions",
            "Epic77-Platform-Signals-and-Notification-System-Implementation-Plan.md");
        var content = File.ReadAllText(path);
        content.Should().Contain("epic77-expected-run-missing-notification-cutover-runbook.md");
    }
}
