using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Pages.Governance;
using Bunit;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>Role detail holders: people are shown as compact user references, in the list and in the delete confirmation.</summary>
public sealed class RoleDetailHoldersUiTests : GovernancePageTestBase
{
    private const string RoleId = "role-day-shift";

    private IRenderedComponent<RoleDetail> RenderRole(OperatorCapabilitiesDto? caps = null)
    {
        Client.Setup(c => c.GetRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleDetailDto(RoleId, "Day shift", "Handles operations", false, [], 1));
        Client.Setup(c => c.GetRoleAssignmentsAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleAssignmentsDto(1, [new RoleAssignmentHolderDto(AliceId, "alice", "Alice Example")]));
        Grant(caps ?? Caps(manageRoles: true));
        var cut = Render<RoleDetail>(p => p.Add(c => c.RoleId, RoleId));
        cut.WaitForAssertion(() => cut.FindAll("#role-assignments .ui-user-ref").Should().NotBeEmpty());
        return cut;
    }

    [Fact]
    public void A_holder_is_a_compact_reference_linked_by_user_id_with_the_display_name_in_its_accessible_name()
    {
        var cut = RenderRole();

        var reference = cut.Find("#role-assignments .ui-user-ref--compact");
        VisibleText(reference).Should().Be("alice");
        AccessibleText(reference).Should().Be("Alice Example (alice)");
        reference.QuerySelector("a")!.GetAttribute("href").Should().Be($"/config/governance/users/{AliceId}");
    }

    [Fact]
    public void The_holder_list_shows_no_raw_user_id()
    {
        var cut = RenderRole();

        VisibleText(cut.Find("#role-assignments")).Should().NotContain(AliceId);
    }

    [Fact]
    public void The_delete_confirmation_lists_blocking_holders_the_same_way()
    {
        var cut = RenderRole();

        cut.Find("#role-delete-trigger").Click();

        cut.WaitForAssertion(() => cut.FindAll("[role='dialog'] .ui-user-ref--compact").Should().ContainSingle());
        var reference = cut.Find("[role='dialog'] .ui-user-ref--compact");
        AccessibleText(reference).Should().Be("Alice Example (alice)");
        reference.QuerySelector("a")!.GetAttribute("href").Should().Be($"/config/governance/users/{AliceId}");
        VisibleText(cut.Find("[role='dialog']")).Should().NotContain(AliceId);
    }

    [Fact]
    public void With_no_holders_and_while_loading_no_user_reference_is_rendered()
    {
        var gate = new TaskCompletionSource<RoleAssignmentsDto>();
        Client.Setup(c => c.GetRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleDetailDto(RoleId, "Day shift", null, false, [], 0));
        Client.Setup(c => c.GetRoleAssignmentsAsync(RoleId, It.IsAny<CancellationToken>())).Returns(gate.Task);
        Grant(Caps(manageRoles: true));

        var cut = Render<RoleDetail>(p => p.Add(c => c.RoleId, RoleId));

        cut.FindAll(".ui-user-ref").Should().BeEmpty();
        gate.SetResult(new RoleAssignmentsDto(0, []));
        cut.WaitForAssertion(() => cut.FindAll("#role-assignments-empty").Should().NotBeEmpty());
        cut.FindAll(".ui-user-ref").Should().BeEmpty();
    }
}
