using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

/// <summary>
/// D7: Management.Api is a class library consumed by Management.Host. It must not ship as a standalone host.
/// </summary>
public sealed class ManagementApiLibraryGuardTests
{
    [Fact]
    public void The_api_project_is_a_class_library_without_an_entry_point()
    {
        var root = RepoRoot();
        var csproj = File.ReadAllText(Path.Combine(root, "src/Apperture.ControlPlane/Apperture.Management.Api/Apperture.Management.Api.csproj"));
        csproj.Should().Contain("<OutputType>Library</OutputType>");
        csproj.Should().Contain("Microsoft.NET.Sdk");
        File.ReadAllText(Path.Combine(root, "src/Apperture.ControlPlane/Apperture.Management.Api/GlobalUsings.cs"))
            .Should().Contain("Microsoft.AspNetCore.Http");
        File.Exists(Path.Combine(root, "src/Apperture.ControlPlane/Apperture.Management.Api/Program.cs")).Should().BeFalse();
        File.Exists(Path.Combine(root, "src/Apperture.ControlPlane/Apperture.Management.Api/Properties/launchSettings.json")).Should().BeFalse();
    }

    [Fact]
    public void Launch_json_no_longer_starts_the_api_as_an_executable()
    {
        var launch = File.ReadAllText(Path.Combine(RepoRoot(), ".vscode/launch.json"));
        launch.Should().NotContain("Apperture.Management.Api.csproj");
        launch.Should().NotContain("\"name\": \"Management API\"");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return dir!.FullName;
    }
}
