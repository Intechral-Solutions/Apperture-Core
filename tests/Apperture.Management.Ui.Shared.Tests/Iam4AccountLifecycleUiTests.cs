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
    public void Profile_distinguishes_directory_email_from_contact_email()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountProfile.razor");

        content.Should().Contain("Microsoft identity email");
        content.Should().Contain("Recovery Access does not use a contact email");
        content.Should().Contain("AccountUiRoutes.Account");
        content.Should().NotContain("id=\"contact-email\"");
        content.Should().NotContain("Current local password");
    }

    [Fact]
    public void Account_hosts_contact_email_inside_the_secured_section()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountSecurity.razor");

        content.Should().Contain("Secured account");
        content.Should().Contain("Contact email");
        content.Should().Contain("Verification pending");
        content.Should().Contain("No contact email configured");
        content.Should().Contain("Verify to manage account settings");
        content.Should().Contain("VerifySecuredAccountAsync");
        content.Should().Contain("autocomplete=\"current-password\"");
        content.Should().Contain("Verify with Microsoft");
        content.Should().Contain("signin/step-up");
        content.Should().NotContain("Current local password");
    }

    [Fact]
    public void Security_shows_local_signin_and_password_as_separate_states()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountSecurity.razor");

        content.Should().Contain("Local sign-in");
        content.Should().Contain("Change password");
        content.Should().Contain("Forgot password");
        content.Should().Contain("does not enable local sign-in");
        content.Should().Contain("PasswordSetupFields");
        content.Should().Contain("ActionButton");
        content.Should().Contain("_passwordFields?.Reset()");
        content.Should().Contain("Session.RefreshAsync()");
        content.Should().Contain("Password updated");
    }

    [Theory]
    [InlineData("Pages/Account/AccountSetupPassword.razor")]
    [InlineData("Pages/Account/AccountResetPassword.razor")]
    [InlineData("Pages/Account/AccountSecurity.razor")]
    [InlineData("Pages/Config/Governance/UserNew.razor")]
    public void New_password_forms_use_confirmation_and_canonical_strength(string relativePath)
    {
        var content = ReadRepoFile($"{ClientRoot}/{relativePath}");

        content.Should().Contain("PasswordSetupFields");
        content.Should().Contain("Confirm");
        content.Should().Contain("ActionButton");
        content.Should().Contain("FormValidationFeedback");
        content.Should().NotContain("uppercase");
        content.Should().NotContain("special character");
    }

    [Fact]
    public void Password_setup_fields_evaluate_locally_and_require_match()
    {
        var molecule = ReadRepoFile($"{ClientRoot}/Molecules/PasswordSetupFields.razor");
        var indicator = ReadRepoFile($"{ClientRoot}/Atoms/PasswordStrengthIndicator.razor");
        var fieldIndicator = ReadRepoFile($"{ClientRoot}/Atoms/FieldValidationIndicator.razor");
        var shell = ReadRepoFile($"{ClientRoot}/Molecules/ValidatedFieldShell.razor");

        molecule.Should().Contain("PasswordSetupSession");
        molecule.Should().Contain("DebouncedPasswordEvaluationScheduler");
        molecule.Should().Contain("@oninput");
        molecule.Should().Contain("Passwords match");
        molecule.Should().Contain("autocomplete=\"new-password\"");
        molecule.Should().Contain("Show password");
        molecule.Should().Contain("Hide password");
        molecule.Should().Contain("EyeIcon");
        molecule.Should().Contain("TogglePasswordVisibility");
        molecule.Should().Contain("ValidateForSubmit");
        molecule.Should().NotContain("ChangeLocalPasswordAsync");
        molecule.Should().NotContain(">Show<");
        molecule.Should().NotContain(">Hide<");
        indicator.Should().Contain("Password strength");
        indicator.Should().Contain("aria-valuetext");
        indicator.Should().Contain("BandLabel");
        indicator.Should().Contain("Neutral");
        fieldIndicator.Should().Contain("FieldValidationPresentation.Pristine");
        fieldIndicator.Should().Contain("role=\"tooltip\"");
        shell.Should().Contain("ui-validated-field__adornments");
        shell.Should().Contain("RenderFragment? Control");
        molecule.Should().NotContain("using Zxcvbn");
        indicator.Should().NotContain("using Zxcvbn");
        fieldIndicator.Should().NotContain("using Zxcvbn");
        shell.Should().NotContain("using Zxcvbn");
    }

    [Fact]
    public void Password_forms_keep_the_action_enabled_and_validate_on_submit()
    {
        var setup = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountSetupPassword.razor");
        var reset = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountResetPassword.razor");
        var security = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountSecurity.razor");
        var create = ReadRepoFile($"{ClientRoot}/Pages/Config/Governance/UserNew.razor");

        setup.Should().Contain("OnInvalidSubmit");
        setup.Should().Contain("ValidateForSubmit");
        setup.Should().NotContain("IsInactive");
        reset.Should().Contain("ValidateForSubmit");
        reset.Should().NotContain("IsInactive");
        security.Should().Contain("ValidateForSubmit");
        security.Should().NotContain("IsInactive=\"@(!CanChangePassword)\"");
        create.Should().Contain("ValidateForSubmit");
        create.Should().NotContain("IsInactive=\"@(!CanCreate)\"");
    }

    [Fact]
    public void Account_mutations_use_action_button_feedback()
    {
        var forgot = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountForgotPassword.razor");
        var profile = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountProfile.razor");
        var detail = ReadRepoFile($"{ClientRoot}/Pages/Config/Governance/UserDetail.razor");

        forgot.Should().Contain("ActionButton");
        forgot.Should().Contain(ForgottenPasswordServiceGenericCopy);
        profile.Should().Contain("GetAccountProfileAsync() ?? _profile");
        var account = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountSecurity.razor");
        account.Should().Contain("ActionButton");
        account.Should().Contain("RequestContactEmailAsync");
        detail.Should().Contain("ActionButton");
        detail.Should().Contain("Resend setup invitation");
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

    private const string ForgottenPasswordServiceGenericCopy =
        "If an eligible account matches, a reset email is on its way.";

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
