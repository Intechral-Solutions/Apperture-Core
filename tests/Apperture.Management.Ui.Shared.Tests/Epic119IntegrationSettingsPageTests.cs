using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic119IntegrationSettingsPageTests
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
    public void IntegrationSettings_page_uses_integration_settings_client_and_auth()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationSettings.razor");
        content.Should().Contain("ListIntegrationSettingsAsync");
        content.Should().Contain("SetIntegrationSettingAsync");
        content.Should().Contain("ResetIntegrationSettingAsync");
        content.Should().Contain("CanUpdateIntegrations");
        content.Should().Contain("CanViewIntegrations");
        content.Should().NotContain("ListPlatformSettingsAsync");
        content.Should().NotContain("SetPlatformSettingAsync");
    }

    [Fact]
    public void IntegrationSettings_page_shows_empty_state_when_no_definitions()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationSettings.razor");
        content.Should().Contain("No settings available");
        content.Should().Contain("_rows.Count == 0");
        content.Should().Contain("Module-specific and integration-scoped settings");
    }

    [Fact]
    public void PlatformSettingRow_loads_integration_options_when_integration_scope()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/PlatformSettingRow.razor");
        content.Should().Contain("GetIntegrationSettingOptionsAsync");
        content.Should().Contain("IntegrationScope");
        content.Should().Contain("GetSourceLabel(Row.Source, IntegrationScope)");
        content.Should().Contain("GetReadOnlyPermissionMessage(IntegrationScope)");
    }

    [Fact]
    public void IntegrationSettings_page_passes_integration_scope_to_rows()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationSettings.razor");
        content.Should().Contain("IntegrationScope=\"true\"");
        content.Should().Contain("IntegrationId=\"@IntegrationId\"");
    }

    [Fact]
    public void IntegrationUiRoutes_exposes_settings_path()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/IntegrationUiRoutes.cs");
        content.Should().Contain("Settings(string integrationId)");
        content.Should().Contain("/settings");
    }
}
