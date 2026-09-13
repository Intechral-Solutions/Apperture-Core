using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic120PlatformSettingsPageTests
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
    public void PlatformSettings_page_uses_category_tabs_and_editor_shell()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/PlatformSettings.razor");
        content.Should().Contain("PlatformSettingsEditor");
        content.Should().Contain("ActiveCategory");
        content.Should().NotContain("ManagementSection");
    }

    [Fact]
    public void PlatformSettingRow_uses_compact_affordances()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/PlatformSettingRow.razor");
        content.Should().Contain("SettingInfoAffordance");
        content.Should().Contain("BuildHelpTooltip");
        content.Should().Contain("Row.IsEditable");
        content.Should().Contain("GetRequiresRestartHint");
    }

    [Fact]
    public void PlatformSettingsEditor_shows_demo_banner()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/PlatformSettingsEditor.razor");
        content.Should().Contain("Settings input gallery (demo)");
        content.Should().Contain("PlatformSettingsCategoryTabs");
    }

    [Fact]
    public void IntegrationSettings_uses_shared_editor_shell()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationSettings.razor");
        content.Should().Contain("PlatformSettingsEditor");
        content.Should().Contain("No settings available");
        content.Should().Contain("IntegrationScope=\"true\"");
    }
}
