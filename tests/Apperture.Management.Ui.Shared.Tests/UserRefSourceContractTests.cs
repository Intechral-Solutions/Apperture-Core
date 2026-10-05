using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// UserRef source contract: it is a presentation primitive only. It fetches nothing, introduces no tooltip or
/// popover, routes by user id, and is not adopted by any product page until the adoption packages land.
/// </summary>
public sealed class UserRefSourceContractTests
{
    private const string UserRefPath = "Molecules/UserRef.razor";

    [Fact]
    public void UserRef_reuses_DrillInLink_and_routes_by_user_id_only()
    {
        var content = ReadComponent(UserRefPath);

        content.Should().Contain("<DrillInLink");
        content.Should().Contain("Uri.EscapeDataString(_user!.UserId)");
        content.Should().NotContain("<a ", "navigation markup comes from DrillInLink, not a second link convention");
        content.Should().NotContain("EscapeDataString(_user!.Username)");
        content.Should().NotContain("NavigationManager");
    }

    [Fact]
    public void UserRef_fetches_nothing_and_owns_no_interaction_state()
    {
        var content = ReadComponent(UserRefPath);

        content.Should().NotContain("@inject");
        content.Should().NotContain("IManagementClient");
        content.Should().NotContain("IOperatorCapabilitiesService");
        content.Should().NotContain("HttpClient");
        content.Should().NotContain("IJSRuntime");
        content.Should().NotContain("@onclick");
        content.Should().NotContain("@onmouse");
        content.Should().NotContain("@onfocus");
    }

    [Fact]
    public void UserRef_introduces_no_tooltip_popover_or_hover_card()
    {
        var content = ReadComponent(UserRefPath) + ReadComponent("Molecules/UserRefMode.cs");
        var css = ReadUserRefCss();

        foreach (var forbidden in new[] { "tooltip", "popover", "hovercard", "hover-card", "role=\"tooltip\"", "aria-describedby" })
        {
            content.ToLowerInvariant().Should().NotContain(forbidden);
            css.ToLowerInvariant().Should().NotContain(forbidden);
        }

        css.Should().NotContain(":hover", "no identity may depend on hover");
    }

    [Fact]
    public void UserRef_does_not_extend_the_reference_contract_with_status_or_avatar()
    {
        var content = ReadComponent(UserRefPath);

        content.Should().NotContain("Avatar");
        content.Should().NotContain("AccountAvatar");
        content.Should().NotContain("UserStatus");
        content.Should().NotContain("StatusPill");
    }

    /// <summary>
    /// IAM-9.4 changes no product page. This guard is deliberately removed by the adoption package (IAM-9.5),
    /// which is the first change allowed to place UserRef on a page.
    /// </summary>
    [Fact]
    public void No_product_page_has_adopted_UserRef_yet()
    {
        var clientRoot = ClientRoot();
        var adopters = Directory
            .EnumerateFiles(clientRoot, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}"))
            .Where(f => !f.EndsWith(Path.Combine("Molecules", "UserRef.razor"), StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("<UserRef"))
            .Select(f => Path.GetRelativePath(clientRoot, f))
            .ToList();

        adopters.Should().BeEmpty("page adoption belongs to a later package");
    }

    private static string ReadUserRefCss()
    {
        var css = File.ReadAllText(Path.Combine(ClientRoot(), "wwwroot", "css", "management-ui-primitives.css"));
        var start = css.IndexOf("/* --- UserRef", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "UserRef styles live in a labelled block");
        var end = css.IndexOf("/* UIX-3", start, StringComparison.Ordinal);
        return css[start..(end > start ? end : css.Length)];
    }

    private static string ReadComponent(string relativePath) =>
        File.ReadAllText(Path.Combine(ClientRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string ClientRoot() =>
        Path.Combine(FindRepoRoot(), "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Apperture.slnx"))
                || File.Exists(Path.Combine(dir.FullName, "Apperture.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root from test base directory.");
    }
}
