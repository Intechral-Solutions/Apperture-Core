using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic112PlatformSettingsPageTests
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
    public void PlatformSettingRow_renders_multi_select_checkbox_editor_and_unsupported_state()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/PlatformSettingRow.razor");
        content.Should().Contain("IsMultiSelect(Row.InputKind)");
        content.Should().Contain("type=\"checkbox\"");
        content.Should().Contain("EditValuesChanged");
        content.Should().Contain("Editor unavailable — no static options defined");
    }

    [Fact]
    public void PlatformSettings_page_sends_values_payload_for_multi_select()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/PlatformSettings.razor");
        content.Should().Contain("EditValues=");
        content.Should().Contain("Values: GetEditValues(row).ToList()");
        content.Should().Contain("_editArrayValues");
    }
}
