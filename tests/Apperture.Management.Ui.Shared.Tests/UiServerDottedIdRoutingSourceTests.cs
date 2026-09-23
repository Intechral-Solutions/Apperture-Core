using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// The default <c>MapFallbackToPage("/_Host")</c> pattern is <c>{*path:nonfile}</c>: it refuses any path whose
/// last segment looks like a file (<c>Apperture.Platform.Sql</c>, <c>Sample.HelloWorld</c>), so a hard load of
/// such a detail URL returns 404 instead of booting the client. Verified against a probe host, the explicit
/// prefix catch-all is what makes these URLs load. This pins that the host keeps registering it.
/// </summary>
public sealed class UiServerDottedIdRoutingSourceTests
{
    private const string Program = "src/Apperture.ControlPlane/Apperture.Management.Ui.Server/Program.cs";

    private static string ReadProgram()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(dir!.FullName, Program.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Theory]
    [InlineData("\"/components\"")]
    [InlineData("\"/modules\"")]
    [InlineData("\"/config/notifications/definitions\"")]
    public void Native_id_detail_families_get_an_explicit_catch_all_fallback(string prefixLiteral)
    {
        var content = ReadProgram();

        content.Should().Contain(prefixLiteral);
        content.Should().Contain("app.MapFallbackToPage(prefix + \"/{*path}\", \"/_Host\");");
    }

    [Fact]
    public void The_default_fallback_is_kept_for_every_other_route()
    {
        ReadProgram().Should().Contain("app.MapFallbackToPage(\"/_Host\");");
    }
}
