using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic114PlatformSettingsPageTests
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
    public void PlatformSettingRow_renders_select_and_loads_provider_options()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/PlatformSettingRow.razor");
        content.Should().Contain("IsSelect(Row.InputKind)");
        content.Should().Contain("GetPlatformSettingOptionsAsync");
        content.Should().Contain("OptionProviderKey");
        content.Should().Contain("Loading options");
        content.Should().Contain("<select");
        content.Should().Contain("unavailable");
    }

    [Fact]
    public void PlatformSettings_page_sends_scalar_payload_for_select()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/PlatformSettings.razor");
        content.Should().Contain("IsMultiSelect(row.InputKind)");
        content.Should().Contain("Value: GetEditValue(row.Key)");
    }
}
