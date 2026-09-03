using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Iam3EmergencyAccessUiSurfaceTests
{
    private const string ClientRoot = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client";

    [Fact]
    public void Auth_overview_presents_emergency_access_and_recovery_access()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Config/Auth/AuthOverview.razor");

        content.Should().Contain("Title=\"Emergency Access\"");
        content.Should().Contain("Account:");
        content.Should().Contain("Username:");
        content.Should().Contain("Recovery Access is omitted from ordinary Governance user administration");
        content.Should().Contain("assume the username is guessable");
        content.Should().NotContain("IAM-3");
        content.Should().NotContain("Super Admin");
    }

    [Fact]
    public void Account_security_hides_microsoft_connect_and_self_password_for_recovery_access()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountSecurity.razor");

        content.Should().Contain("!_me.EmergencyAccessAccount");
        content.Should().Contain("Connect Microsoft account");
        content.Should().Contain("Password changes for Recovery Access use Emergency Access operator tooling");
        content.Should().NotContain("IAM-3");
        content.Should().NotContain("IAM-4");
    }

    [Fact]
    public void Account_profile_treats_recovery_access_display_name_as_not_editable()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountProfile.razor");

        content.Should().Contain("DisplayNameEditable");
        content.Should().Contain("Recovery Access is system-managed");
        content.Should().Contain("Avatar customization is not available for Recovery Access");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return dir!.FullName;
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath));
}
