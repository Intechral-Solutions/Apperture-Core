using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Iam5EntraProvisioningUiSurfaceTests
{
    private const string ClientRoot = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client";

    [Fact]
    public void Users_list_exposes_entra_provisioning_and_requests()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Governance/UsersList.razor");
        content.Should().Contain("Add from Microsoft Entra");
        content.Should().Contain("/config/governance/users/from-entra");
        content.Should().Contain("/config/governance/users/requests");
        content.Should().Contain("New user");
        content.Should().NotContain("IAM-5");
        content.Should().NotContain("SCIM");
    }

    [Fact]
    public void Directory_picker_searches_and_batches_without_graph_tokens()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Config/Governance/UserFromEntra.razor");
        content.Should().Contain("@page \"/config/governance/users/from-entra\"");
        content.Should().Contain("SearchEntraProvisioningDirectoryAsync");
        content.Should().Contain("ProvisionFromEntraDirectoryAsync");
        content.Should().Contain("Add 1 person");
        content.Should().Contain("Send welcome email");
        content.Should().NotContain("clientSecret");
        content.Should().NotContain("Graph");
        content.Should().NotContain("IAM-5");
    }

    [Fact]
    public void Access_request_review_approves_and_rejects()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Config/Governance/UserAccessRequests.razor");
        content.Should().Contain("@page \"/config/governance/users/requests\"");
        content.Should().Contain("Approve access");
        content.Should().Contain("Reject");
        content.Should().Contain("TechnicalDetails");
        content.Should().NotContain("IAM-5");
    }

    [Fact]
    public void Limited_access_request_page_is_public_and_does_not_offer_roles()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/AccessRequest.razor");
        content.Should().Contain("@page \"/access-request\"");
        content.Should().Contain("Request access");
        content.Should().Contain("You don't have access to Apperture yet");
        content.Should().NotContain("GetUserPageAsync");
        content.Should().NotContain("role-platform-administrator");
        content.Should().NotContain("IAM-5");
    }

    [Fact]
    public void Entra_tenant_setup_exposes_access_request_policy()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Config/Auth/EntraTenants.razor");
        content.Should().Contain("Allow directory members to request Apperture access");
        content.Should().Contain("AllowMemberAccessRequests");
        content.Should().Contain("AllowGuestAccessRequests");
        content.Should().NotContain("IAM-5");
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
