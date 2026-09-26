using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Ui.Server serves the shell page and the WebAssembly assets; it renders no component (no prerender). The only Management UI
/// HTTP pipeline is the one in Ui.Client's <c>Program.cs</c>, where the platform request header is added. Ui.Server must not grow
/// a second, header-less pipeline (design gate §22).
/// </summary>
public sealed class UiServerHostRegistrationGuardTests
{
    private const string Program = "src/Apperture.ControlPlane/Apperture.Management.Ui.Server/Program.cs";

    [Theory]
    [InlineData("HttpClient")]
    [InlineData("IManagementClient")]
    [InlineData("ManagementClient")]
    [InlineData("IncludeCredentialsHandler")]
    [InlineData("DomainMenuService")]
    [InlineData("IOperatorCapabilitiesService")]
    [InlineData("AddAppertureShellServices")]
    [InlineData("ApiBaseUrl")]
    public void The_ui_server_registers_no_management_api_client(string forbidden)
    {
        ReadProgram().Should().NotContain(forbidden);
    }

    [Fact]
    public void The_ui_server_still_serves_the_webassembly_shell()
    {
        var program = ReadProgram();
        program.Should().Contain("app.UseBlazorFrameworkFiles();");
        program.Should().Contain("app.MapFallbackToPage(\"/_Host\");");
    }

    [Fact]
    public void The_ui_server_startup_writes_no_debug_log_file()
    {
        var program = ReadProgram();
        program.Should().NotContain("debug-4bd3d8.log");
        program.Should().NotContain("File.AppendAllText");
        program.Should().NotContain("appertureShell.debugLog");
    }

    private static string ReadProgram()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(dir!.FullName, Program.Replace('/', Path.DirectorySeparatorChar)));
    }
}
