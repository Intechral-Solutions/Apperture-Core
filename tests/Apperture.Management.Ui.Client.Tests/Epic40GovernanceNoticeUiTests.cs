using System.Text.RegularExpressions;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Pages.Governance;
using Bunit;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// Epic 40 truthfulness notices, read from the rendered pages: replace-all role editing and role-derived access. An operator
/// decides from this copy, so each test fails when the notice is removed, weakened or contradicted in the real component.
/// </summary>
public sealed class Epic40GovernanceNoticeUiTests : GovernancePageTestBase
{
    private const string RoleId = "role-day-shift";
    private const string KeepKey = "user.read|User|instance";
    private const string DropKey = "audit.read|AuditPolicy|instance";

    private IRenderedComponent<RoleDetail> RenderEditableRole()
    {
        Client.Setup(c => c.GetRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleDetailDto(RoleId, "Day shift", "Handles operations", false,
                [new RolePermissionEntryDto("user.read", "User", "instance"), new RolePermissionEntryDto("audit.read", "AuditPolicy", "instance")], 0));
        Client.Setup(c => c.GetRoleAssignmentsAsync(RoleId, It.IsAny<CancellationToken>())).ReturnsAsync(new RoleAssignmentsDto(0, []));
        Client.Setup(c => c.GetGovernanceDescriptorsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GovernanceDescriptorResponse(
                [],
                [
                    new PermissionCatalogEntryDto("user.read", "User", "instance", "Core", "platform", null,
                        "View users", "View users, their roles and how they sign in.", "AccessControl", "Standard", false),
                    new PermissionCatalogEntryDto("audit.read", "AuditPolicy", "instance", "Core", "platform", null,
                        "View audit", "View the audit trail.", "Audit", "Standard", false)
                ]));
        Grant(Caps(manageRoles: true) with { CanAssignPermissions = true });
        var cut = Render<RoleDetail>(p => p.Add(c => c.RoleId, RoleId));
        cut.WaitForAssertion(() => EditButton(cut).Should().NotBeNull());
        return cut;
    }

    private static AngleSharp.Dom.IElement? EditButton(IRenderedComponent<RoleDetail> cut) =>
        cut.FindAll("button").SingleOrDefault(b => b.TextContent.Trim() == "Edit permissions");

    private static string Squash(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    [Fact]
    public void Editing_a_role_warns_that_saving_replaces_the_whole_set_and_revokes_what_is_not_selected()
    {
        var cut = RenderEditableRole();
        Squash(VisibleText(cut.Find("div.p-4"))).Should().NotContain("Saving replaces", "the warning belongs to edit mode, not the read view");

        EditButton(cut)!.Click();

        cut.WaitForElement(".border-message-warning");
        var notice = Squash(VisibleText(cut.Find(".border-message-warning")));
        notice.Should().Contain("Saving replaces the role's entire permission set", "replace-all must be explicit");
        notice.Should().Contain("Permissions not selected will be revoked", "revocation by omission must be explicit");
        notice.Should().Contain("Changes apply to the role as currently loaded", "the edit is against the role as loaded, not as it may now be");
    }

    [Fact]
    public void Saving_a_role_sends_exactly_the_selected_permissions_so_an_omitted_one_is_revoked()
    {
        var cut = RenderEditableRole();
        EditButton(cut)!.Click();
        cut.WaitForAssertion(() => cut.FindAll("input[type=checkbox]").Should().HaveCount(2));
        cut.FindAll("input[type=checkbox]").Single(i => i.Id == KeepKey).HasAttribute("checked").Should().BeTrue("the role holds it today");
        cut.FindAll("input[type=checkbox]").Single(i => i.Id == DropKey).HasAttribute("checked").Should().BeTrue("the role holds it today");

        cut.FindAll("input[type=checkbox]").Single(i => i.Id == DropKey).Change(false);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save").Click();

        cut.WaitForAssertion(() => Client.Verify(c => c.SetRolePermissionsAsync(
            RoleId,
            It.Is<SetRolePermissionsRequest>(r => r.Permissions.Count == 1 && r.Permissions[0].Action == "user.read" && r.Permissions[0].ResourceType == "User"),
            It.IsAny<CancellationToken>()), Times.Once));
    }

    [Fact]
    public void Role_derived_access_states_what_it_covers_and_what_it_excludes()
    {
        Client.Setup(c => c.GetUserAsync(AliceId, It.IsAny<CancellationToken>())).ReturnsAsync(Alice());
        Client.Setup(c => c.GetEffectiveAccessAsync(AliceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrincipalEffectiveAccessDto(AliceId, ["role-ops"], [new RolePermissionEntryDto("user.read", "User", "instance")]));
        Grant(Caps());

        var cut = Render<UserEffectiveAccess>(p => p.Add(c => c.UserId, AliceId));

        var notice = Squash(VisibleText(cut.Find(".ui-settings-callout")));
        notice.Should().Contain("merged permissions from assigned roles only", "the source of the list must be stated");
        notice.Should().Contain("It is not a complete effective-access model", "the page must not overclaim");
        notice.Should().Contain("direct user grants, scope containment, and service principals are excluded", "the omissions must be stated, not implied");
    }
}
