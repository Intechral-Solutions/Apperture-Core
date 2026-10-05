using Apperture.Management.Client;
using Bunit;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>Users list: username as the link, display name beside it, search, and no raw ids in the visible content.</summary>
public sealed class UsersListUiTests : GovernancePageTestBase
{
    private static UserListDto Row(string id, string username, string display, int roles = 1) =>
        new(id, username, display, HasLocalLink: true, HasEntraLink: false, RoleIds: Enumerable.Range(0, roles).Select(i => $"role-{i}").ToArray());

    private void ListReturns(params UserListDto[] rows) =>
        Client.Setup(c => c.GetUserPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(new UserListResponse(rows, rows.Length));

    private IRenderedComponent<Apperture.Management.Ui.Client.Pages.Governance.UsersList> RenderList(OperatorCapabilitiesDto? caps = null)
    {
        Grant(caps ?? Caps());
        return Render<Apperture.Management.Ui.Client.Pages.Governance.UsersList>();
    }

    [Fact]
    public void Each_row_shows_the_username_as_a_compact_user_reference_linked_by_user_id_and_the_display_name_as_text()
    {
        ListReturns(Row(AliceId, "alice", "Alice Example"));

        var cut = RenderList();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().HaveCount(1));

        var row = cut.Find("tbody tr");
        var reference = row.QuerySelector(".ui-user-ref--compact")!;
        VisibleText(reference).Should().Be("alice");
        AccessibleText(reference).Should().Be("Alice Example (alice)");
        reference.QuerySelector("a")!.GetAttribute("href").Should().Be($"/config/governance/users/{AliceId}");
        row.QuerySelectorAll("td")[1].TextContent.Trim().Should().Be("Alice Example");
        row.QuerySelectorAll("td")[1].QuerySelector("a").Should().BeNull("the identity link is the username; the display name is plain text");
    }

    [Fact]
    public void The_raw_user_id_is_not_visible_text_anywhere_on_the_page()
    {
        ListReturns(Row(AliceId, "alice", "Alice Example"));

        var cut = RenderList();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().HaveCount(1));

        VisibleText(cut.Find("table")).Should().NotContain(AliceId);
        cut.Find("thead").TextContent.Should().Contain("Username").And.NotContain("Id");
    }

    [Fact]
    public void Row_actions_are_kept_and_no_interactive_element_is_nested_in_another()
    {
        ListReturns(Row(AliceId, "alice", "Alice Example"));

        var cut = RenderList();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().HaveCount(1));

        var row = cut.Find("tbody tr");
        row.TextContent.Should().Contain("Manage roles").And.Contain("View role-derived access");
        row.QuerySelectorAll("a a, a button, button a, button button").Should().BeEmpty();
        row.QuerySelectorAll("a").Select(a => a.GetAttribute("href")).Should().Contain(
        [
            $"/config/governance/users/{AliceId}",
            $"/config/governance/users/{AliceId}/role-derived-access",
        ]);
    }

    [Fact]
    public void Searching_asks_the_server_for_the_trimmed_query_from_the_first_page()
    {
        ListReturns(Row(AliceId, "alice", "Alice Example"));
        var cut = RenderList();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().HaveCount(1));

        cut.Find("input#users-search").Input("  ali  ");
        cut.Find("form[role='search']").Submit();

        Client.Verify(c => c.GetUserPageAsync(0, 20, It.IsAny<CancellationToken>(), "ali"), Times.Once);
    }

    [Fact]
    public void An_empty_search_result_says_nothing_matched_and_keeps_the_search_box_to_change_it()
    {
        ListReturns(Row(AliceId, "alice", "Alice Example"));
        var cut = RenderList();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().HaveCount(1));
        Client.Setup(c => c.GetUserPageAsync(0, 20, It.IsAny<CancellationToken>(), "zzz"))
            .ReturnsAsync(new UserListResponse([], 0));

        cut.Find("input#users-search").Input("zzz");
        cut.Find("form[role='search']").Submit();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No users match"));
        cut.FindAll("input#users-search").Should().ContainSingle();
        cut.Find("input#users-search").GetAttribute("value").Should().Be("zzz");
    }

    [Fact]
    public void Clearing_the_search_returns_to_the_unfiltered_list()
    {
        ListReturns(Row(AliceId, "alice", "Alice Example"));
        var cut = RenderList();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().HaveCount(1));
        cut.Find("input#users-search").Input("ali");
        cut.Find("form[role='search']").Submit();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Clear").Click();

        Client.Verify(c => c.GetUserPageAsync(0, 20, It.IsAny<CancellationToken>(), null), Times.AtLeast(2));
        cut.Find("input#users-search").GetAttribute("value").Should().BeNullOrEmpty();
    }

    [Fact]
    public void Paging_keeps_the_search_and_moves_by_the_page_size()
    {
        var rows = Enumerable.Range(0, 20).Select(i => Row($"{i:x24}", $"user{i:00}", $"User {i}")).ToArray();
        Client.Setup(c => c.GetUserPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(new UserListResponse(rows, 45));
        var cut = RenderList();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().HaveCount(20));
        cut.Find("input#users-search").Input("user");
        cut.Find("form[role='search']").Submit();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Next").Click();

        Client.Verify(c => c.GetUserPageAsync(20, 20, It.IsAny<CancellationToken>(), "user"), Times.Once);
    }

    [Fact]
    public void Without_governance_access_the_page_is_forbidden_and_shows_no_identity_or_search()
    {
        ListReturns(Row(AliceId, "alice", "Alice Example"));

        var cut = RenderList(Caps(view: false));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("You do not have permission to view users."));
        cut.FindAll("input#users-search").Should().BeEmpty();
        cut.Markup.Should().NotContain("alice");
        Client.Verify(c => c.GetUserPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void While_loading_and_when_empty_no_user_reference_is_rendered()
    {
        var gate = new TaskCompletionSource<UserListResponse>();
        Client.Setup(c => c.GetUserPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .Returns(gate.Task);

        var cut = RenderList();

        cut.FindAll(".ui-user-ref").Should().BeEmpty("nothing to reference while the list loads");
        gate.SetResult(new UserListResponse([], 0));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No users found."));
        cut.FindAll(".ui-user-ref").Should().BeEmpty();
    }
}
