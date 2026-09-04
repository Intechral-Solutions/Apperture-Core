using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Iam4AccountLifecycleUiTests
{
    private const string ClientRoot = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client";

    [Theory]
    [InlineData("Pages/Account/AccountVerifyEmail.razor", "/account/verify-email")]
    [InlineData("Pages/Account/AccountSetupPassword.razor", "/account/setup-password")]
    [InlineData("Pages/Account/AccountResetPassword.razor", "/account/reset-password")]
    [InlineData("Pages/Account/AccountForgotPassword.razor", "/account/forgot-password")]
    public void Public_account_action_pages_use_auth_layout_and_generic_errors(string relativePath, string route)
    {
        var content = ReadRepoFile($"{ClientRoot}/{relativePath}");

        content.Should().Contain($"@page \"{route}\"");
        content.Should().Contain("@layout AuthLayout");
        content.Should().NotContain("<AccountSectionNav");
        content.Should().NotContain("IAM-4");
        content.Should().NotContain("breakglass");
    }

    [Fact]
    public void Profile_distinguishes_contact_email_from_directory_email()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountProfile.razor");

        content.Should().Contain("Contact email");
        content.Should().Contain("Microsoft identity email");
        content.Should().Contain("Verification pending");
        content.Should().Contain("No contact email configured");
        content.Should().Contain("Recovery Access does not use a contact email");
        content.Should().Contain("HasEnabledLocalPassword");
        content.Should().Contain("Current local password");
    }

    [Fact]
    public void Security_shows_local_signin_and_password_as_separate_states()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountSecurity.razor");

        content.Should().Contain("Local sign-in");
        content.Should().Contain("Change password");
        content.Should().Contain("Forgot password");
        content.Should().Contain("does not enable local sign-in");
    }

    [Fact]
    public void New_user_defaults_to_setup_invitation()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Config/Governance/UserNew.razor");

        content.Should().Contain("Send account setup invitation");
        content.Should().Contain("Set an initial password now");
        content.Should().Contain("_sendInvitation = true");
        content.Should().Contain("not a Microsoft Entra invitation");
    }

    [Fact]
    public void User_detail_shows_lifecycle_states_without_token_material()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Config/Governance/UserDetail.razor");

        content.Should().Contain("Contact email");
        content.Should().Contain("Setup pending");
        content.Should().Contain("Resend setup invitation");
        content.Should().Contain("Account setup is not finished");
        content.Should().Contain("CanSendSetupInvitation");
        content.Should().Contain("HasLocalCredential != true");
        content.Should().NotContain("token=");
        content.Should().NotContain("PasswordHash");
    }

    [Theory]
    [InlineData("/account/verify-email", "Verify contact email")]
    [InlineData("/account/setup-password", "Set up password")]
    [InlineData("/account/reset-password", "Reset password")]
    [InlineData("/account/forgot-password", "Forgot password")]
    public void Public_account_action_paths_have_breadcrumbs(string path, string title)
    {
        var orientation = ManagementBreadcrumbResolver.ResolveOrientation(path);
        orientation.Title.Should().Be(title);
    }

    [Fact]
    public void Public_account_action_paths_are_not_account_sections()
    {
        AccountUiRoutes.IsAccountPath(AccountUiRoutes.VerifyEmail).Should().BeFalse();
        AccountUiRoutes.IsAccountPath(AccountUiRoutes.SetupPassword).Should().BeFalse();
        AccountUiRoutes.IsAccountPath(AccountUiRoutes.ForgotPassword).Should().BeFalse();
        LoginRouteHelper.IsPublicPath(AccountUiRoutes.VerifyEmail).Should().BeTrue();
        LoginRouteHelper.IsLoginChooserPath(AccountUiRoutes.VerifyEmail).Should().BeFalse();
    }

    private static string ReadRepoFile(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(FindRepoRoot(), relativePath));
        File.Exists(path).Should().BeTrue($"expected {relativePath} to exist");
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
        return dir.FullName;
    }
}
