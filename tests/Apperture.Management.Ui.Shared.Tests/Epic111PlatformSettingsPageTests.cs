using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic111PlatformSettingsPageTests
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
    public void PlatformSettings_page_includes_operator_guidance_and_audit_note()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/PlatformSettings.razor");
        content.Should().Contain("How platform settings work");
        content.Should().Contain("platform override");
        content.Should().Contain("never write back to appsettings");
        content.Should().Contain("platform.settings.set");
        content.Should().Contain("/control/audit");
    }

    [Fact]
    public void PlatformSettings_page_groups_by_category_and_uses_row_component()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/PlatformSettings.razor");
        content.Should().Contain("ManagementSection");
        content.Should().Contain("PlatformSettingUiHelpers.GetCategoryTitle");
        content.Should().Contain("<PlatformSettingRow");
    }

    [Fact]
    public void PlatformSettings_page_handles_capabilities_and_reset_confirm()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/PlatformSettings.razor");
        content.Should().Contain("ForbiddenState");
        content.Should().Contain("CanManagePlatformSettings");
        content.Should().Contain("read-only access");
        content.Should().Contain("\"confirm\"");
        content.Should().Contain("IsPlatformOverride");
    }

    [Fact]
    public void PlatformSettingRow_uses_status_pill_and_dirty_save_guard()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/PlatformSettingRow.razor");
        content.Should().Contain("<StatusPill");
        content.Should().Contain("PlatformSettingUiHelpers.GetSourceLabel");
        content.Should().Contain("disabled=\"@(!IsDirty || IsSaving)\"");
        content.Should().Contain("type=\"checkbox\"");
        content.Should().Contain("Read-only");
    }
}
