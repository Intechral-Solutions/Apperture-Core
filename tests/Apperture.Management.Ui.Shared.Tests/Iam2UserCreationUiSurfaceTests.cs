using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Iam2UserCreationUiSurfaceTests
{
    private const string ClientRoot = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client";

    [Fact]
    public void Users_list_exposes_new_user_when_the_operator_can_manage_users()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Governance/UsersList.razor");

        content.Should().Contain("CanManageUsers");
        content.Should().Contain("/config/governance/users/new");
        content.Should().Contain("New user");
    }

    [Fact]
    public void New_user_page_is_a_literal_route_and_creates_a_local_user()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Config/Governance/UserNew.razor");

        content.Should().Contain("@page \"/config/governance/users/new\"");
        content.Should().Contain("CanManageUsers");
        content.Should().Contain("CreateUserAsync");
        content.Should().Contain("\"Local\"");
        content.Should().Contain("AssignRoleAsync");
        content.Should().Contain("CanAssignRoles");
        content.Should().NotContain("IAM-2");
        content.Should().NotContain("IAM-4");
        content.Should().NotContain("first-admin");
        content.Should().Contain("PasswordSetupFields");
        content.Should().Contain("Set an initial password now");
    }

    [Fact]
    public void New_user_username_is_checked_live_through_the_shared_field_and_sent_as_the_preferred_name()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Config/Governance/UserNew.razor");

        content.Should().Contain("<UsernameField");
        content.Should().Contain("UsernameRules.ToFieldValidation(_username)");
        content.Should().Contain("Username: _username.Trim()");
        content.Should().NotContain("@bind=\"_username\"", "the raw input is replaced by the validated field");
    }

    [Fact]
    public void Users_list_identifies_people_by_username_and_offers_search()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Governance/UsersList.razor");

        content.Should().Contain("<UserRef");
        content.Should().Contain("UserRefMode.Compact");
        content.Should().Contain("CanNavigate=\"@(_capabilities?.CanViewGovernance == true)\"");
        content.Should().Contain("role=\"search\"");
        content.Should().Contain("<th>Username</th>");
        content.Should().NotContain("<th>Id</th>");
    }

    [Fact]
    public void User_detail_uses_the_rich_identity_keeps_the_id_in_technical_details_and_offers_rename()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Config/Governance/UserDetail.razor");

        content.Should().Contain("UserRefMode.Rich");
        content.Should().Contain("Label=\"Username\"");
        content.Should().Contain("<TechnicalDetail Label=\"User ID\"");
        content.Should().NotContain("<MetadataItem Label=\"User ID\"");
        content.Should().Contain("RenameUserAsync");
        content.Should().Contain("CanManageUsers");
        content.Should().Contain("aria-busy");
    }

    /// <summary>
    /// The targeted governance surfaces never print a raw user id as content. The id may appear only inside
    /// <c>TechnicalDetails</c>, or in code (routes and requests).
    /// </summary>
    [Theory]
    [InlineData("Pages/Governance/UsersList.razor")]
    [InlineData("Pages/Config/Governance/UserDetail.razor")]
    [InlineData("Pages/Governance/UserEffectiveAccess.razor")]
    [InlineData("Pages/Governance/RoleDetail.razor")]
    [InlineData("Molecules/UserAccountMenu.razor")]
    public void Targeted_governance_surfaces_do_not_print_the_raw_user_id_outside_technical_details(string relativePath)
    {
        var content = ReadRepoFile($"{ClientRoot}/{relativePath}");
        var withoutTechnicalDetails = System.Text.RegularExpressions.Regex.Replace(
            content, @"<TechnicalDetails\b.*?</TechnicalDetails>", "", System.Text.RegularExpressions.RegexOptions.Singleline);

        var printedAsText = System.Text.RegularExpressions.Regex.Matches(
            withoutTechnicalDetails, @">\s*@[\w\.\!\?]*UserId\b[^<]*<|Value=""@[\w\.\!\?]*UserId""");

        printedAsText.Select(m => m.Value).Should().BeEmpty("the user id is technical detail, not identity");
    }

    [Fact]
    public void Role_detail_no_longer_builds_its_own_user_links()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Governance/RoleDetail.razor");

        content.Should().Contain("<UserRef");
        content.Should().NotContain("UserHref(");
    }

    [Fact]
    public void Account_menu_falls_back_to_the_username_not_the_user_id()
    {
        var content = ReadRepoFile($"{ClientRoot}/Molecules/UserAccountMenu.razor");

        content.Should().Contain("return user.Username;");
        content.Should().NotContain("return user.UserId;");
    }

    [Fact]
    public void User_detail_route_does_not_claim_the_new_user_path()
    {
        var detail = ReadRepoFile($"{ClientRoot}/Pages/Config/Governance/UserDetail.razor");
        var create = ReadRepoFile($"{ClientRoot}/Pages/Config/Governance/UserNew.razor");

        detail.Should().Contain("@page \"/config/governance/users/{UserId}\"");
        create.Should().Contain("@page \"/config/governance/users/new\"");
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
