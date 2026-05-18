using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic77CutoverReadinessUiTests
{
    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull("repository root not found from test output directory");
        var path = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"expected source at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void IntegrationNotifications_includes_staging_validation_section()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationNotifications.razor");
        content.Should().Contain("Staging cutover validation");
        content.Should().Contain("GetExpectedRunMissingCutoverValidationAsync");
        content.Should().Contain("Recipient parity gap");
    }

    [Fact]
    public void ScheduleDiagnostics_labels_legacy_ledger()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationScheduleDiagnostics.razor");
        content.Should().Contain("legacy");
    }

    [Fact]
    public void IntegrationNotifications_includes_operator_sign_off_section()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationNotifications.razor");
        content.Should().Contain("Operator sign-off");
        content.Should().Contain("Sign off — ready for cutover");
        content.Should().Contain("Revoke sign-off");
        content.Should().Contain("GetExpectedRunMissingCutoverSignOffAsync");
    }

    [Fact]
    public void IntegrationNotifications_includes_cutover_readiness_panel()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationNotifications.razor");
        content.Should().Contain("Expected-run-missing cutover readiness");
        content.Should().Contain("id=\"cutover-readiness\"");
        content.Should().Contain("Cutover checklist (display only)");
        content.Should().Contain("ExpectedRunMissingCutoverConfigWarnings");
        content.Should().Contain("ReadinessLabel");
        content.Should().Contain("LoadCutoverReadinessAsync");
    }

    [Fact]
    public void IntegrationNotifications_view_does_not_require_CanUpdateIntegrations()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationNotifications.razor");
        content.Should().Contain("_canView = caps.CanViewIntegrations && caps.CanViewNotificationDefinitions");
        content.Should().Contain("_canManageSubscriptions = caps.CanUpdateIntegrations");
    }

    [Fact]
    public void NotificationDiagnostics_shows_cutover_sign_off_when_loaded()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDiagnostics.razor");
        content.Should().Contain("Latest sign-off");
        content.Should().Contain("LoadCutoverSignOffAsync");
    }

    [Fact]
    public void NotificationDiagnostics_includes_cutover_section_and_integration_link()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDiagnostics.razor");
        content.Should().Contain("Expected-run-missing cutover (instance)");
        content.Should().Contain("Open integration cutover readiness");
        content.Should().Contain("RecentPendingOrDeliveringOutboxCount");
        content.Should().Contain("CanViewIntegrations && caps.CanViewNotificationDefinitions");
    }

    [Fact]
    public void ScheduleDiagnostics_links_to_cutover_readiness()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationScheduleDiagnostics.razor");
        content.Should().Contain("View platform notification cutover readiness");
        content.Should().Contain("/notifications#cutover-readiness");
    }
}
