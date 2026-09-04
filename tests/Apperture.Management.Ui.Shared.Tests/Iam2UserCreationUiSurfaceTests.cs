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
