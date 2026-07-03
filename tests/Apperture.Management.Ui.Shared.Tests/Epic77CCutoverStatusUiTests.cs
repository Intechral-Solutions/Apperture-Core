using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic77CCutoverStatusUiTests
{
    [Fact]
    public void Delivery_page_includes_cutover_status_card()
    {
        var delivery = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDelivery.razor");
        delivery.Should().Contain("NotificationPipelineCutoverStatusCard");
        delivery.Should().Contain("GetNotificationPipelineCutoverStatusAsync");
    }

    [Fact]
    public void Diagnostics_page_includes_cutover_status_card()
    {
        var diagnostics = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDiagnostics.razor");
        diagnostics.Should().Contain("NotificationPipelineCutoverStatusCard");
        diagnostics.Should().Contain("GetNotificationPipelineCutoverStatusAsync");
    }

    [Fact]
    public void Cutover_status_card_shows_preflight_and_rollback_hint()
    {
        var card = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Molecules/NotificationPipelineCutoverStatusCard.razor");
        card.Should().Contain("Preflight checks");
        card.Should().Contain("Active delivery path");
        card.Should().Contain("UsePlatformPipeline:Enabled=false");
        card.Should().Contain("S9");
        card.Should().Contain("S10");
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath));
    }
}
