using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Every sign-out control is the one <c>SignOutForm</c> (a native form POST to the host, design gate D2). Nothing navigates to
/// the sign-out URL, because a GET no longer signs out and must never be how the UI tries to.
/// </summary>
public sealed class SignOutFormSourceGuardTests
{
    private const string ClientRoot = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client";

    private static readonly string[] SignOutSurfaces =
    [
        $"{ClientRoot}/Molecules/UserAccountMenu.razor",
        $"{ClientRoot}/Pages/AccessRequest.razor",
        $"{ClientRoot}/Pages/Account/AccountSecurity.razor",
        $"{ClientRoot}/Organisms/SessionStatusSurface.razor",
    ];

    [Fact]
    public void Each_sign_out_surface_uses_the_sign_out_form()
    {
        foreach (var surface in SignOutSurfaces)
            Read(surface).Should().Contain("<SignOutForm", $"{surface} signs out with a form POST");
    }

    [Fact]
    public void Only_the_sign_out_form_knows_the_sign_out_url()
    {
        var users = ClientFiles()
            .Where(file => File.ReadAllText(file).Contains("GetSignOutUrl", StringComparison.Ordinal))
            .Select(Relative)
            .OrderBy(path => path)
            .ToArray();

        users.Should().Equal(
            $"{ClientRoot}/Atoms/SignOutForm.razor",
            $"{ClientRoot}/Services/ShellHostNavigation.cs");
    }

    [Fact]
    public void The_sign_out_form_posts_and_carries_no_script_handler()
    {
        var form = Read($"{ClientRoot}/Atoms/SignOutForm.razor");
        form.Should().Contain("<form method=\"post\"").And.Contain("type=\"submit\"");
        form.Should().NotContain("@onsubmit").And.NotContain("@onclick").And.NotContain("NavigateTo");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative));

    private static IEnumerable<string> ClientFiles() =>
        SourceTree.Files(ClientRoot, [".razor", ".cs"], includeMigrations: true, SourceTree.Floor.UiClientSource);

    private static string Relative(string path) => Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');

    private static string RepoRoot() => RepositoryRoot.Find();
}
