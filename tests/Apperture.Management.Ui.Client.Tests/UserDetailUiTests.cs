using System.Net;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Pages.Config.Governance;
using Bunit;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>User detail: rich identity in the header, username as metadata, the id kept to technical details, and rename.</summary>
public sealed class UserDetailUiTests : GovernancePageTestBase
{
    private IRenderedComponent<UserDetail> RenderDetail(OperatorCapabilitiesDto? caps = null, UserDetailDto? user = null)
    {
        Client.Setup(c => c.GetUserAsync(AliceId, It.IsAny<CancellationToken>())).ReturnsAsync(user ?? Alice());
        Client.Setup(c => c.GetRolesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<RoleDto>());
        Client.Setup(c => c.GetEntraTenantsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<EntraTenantListItemDto>());
        Grant(caps ?? Caps(manageUsers: true));
        var cut = Render<UserDetail>(p => p.Add(c => c.UserId, AliceId));
        cut.WaitForAssertion(() => cut.FindAll(".ui-user-ref").Should().NotBeEmpty());
        return cut;
    }

    private static AngleSharp.Dom.IElement Field(IRenderedComponent<UserDetail> cut) => cut.Find("input#rename-username");

    private static AngleSharp.Dom.IElement RenameForm(IRenderedComponent<UserDetail> cut) => cut.Find("form[data-rename-username]");

    // ---------- Identity presentation ----------

    [Fact]
    public void The_header_shows_the_rich_identity_as_plain_text_because_it_is_the_page_the_link_would_open()
    {
        var cut = RenderDetail();

        var identity = cut.Find(".ui-page-header .ui-user-ref--rich");
        VisibleText(identity).Should().Be("Alice Example (alice)");
        AccessibleText(identity).Should().Be("Alice Example (alice)");
        identity.QuerySelector("a").Should().BeNull("a link from the page to itself would be a no-op focus stop");
    }

    [Fact]
    public void The_raw_user_id_is_not_visible_outside_technical_details()
    {
        var cut = RenderDetail();

        VisibleTextOutsideTechnicalDetails(cut.Find("div.p-4")).Should().NotContain(AliceId);
    }

    [Fact]
    public void The_user_id_lives_in_collapsed_technical_details()
    {
        var cut = RenderDetail();

        var details = cut.Find(".ui-technical-details");
        details.QuerySelector("dt")!.TextContent.Should().Be("User ID");
        details.QuerySelector("dd")!.TextContent.Trim().Should().Be(AliceId);
        details.HasAttribute("open").Should().BeFalse("technical details start collapsed");
    }

    [Fact]
    public void The_summary_shows_the_username_instead_of_the_user_id()
    {
        var cut = RenderDetail();

        var labels = cut.FindAll(".ui-metadata-item").ToDictionary(
            i => i.QuerySelector(".ui-metadata-item__label")!.TextContent.Trim(),
            i => i.QuerySelector(".ui-metadata-item__value")!.TextContent.Trim());
        labels.Should().ContainKey("Username").WhoseValue.Should().Be("alice");
        labels.Should().NotContainKey("User ID", "the id is not summary content");
    }

    // ---------- Rename availability ----------

    [Fact]
    public void Rename_is_offered_to_user_manage_with_the_current_username_prefilled()
    {
        var cut = RenderDetail();

        Field(cut).GetAttribute("value").Should().Be("alice");
        cut.Find("label[for='rename-username']").TextContent.Should().Contain("Username");
        RenameForm(cut).TextContent.Should().Contain("Change username");
    }

    [Fact]
    public void Rename_is_not_offered_without_user_manage()
    {
        var cut = RenderDetail(Caps(manageUsers: false));

        cut.FindAll("input#rename-username").Should().BeEmpty();
        cut.FindAll("form[data-rename-username]").Should().BeEmpty();
    }

    [Fact]
    public void Rename_is_not_offered_for_the_system_managed_recovery_access_account()
    {
        var cut = RenderDetail(user: Alice(username: "breakglass", emergency: true, displayName: "Recovery Access"));

        cut.FindAll("input#rename-username").Should().BeEmpty();
        cut.Markup.Should().Contain("system-managed");
    }

    // ---------- Rename flow ----------

    [Fact]
    public void A_confirmed_rename_reloads_the_user_from_the_server_and_shows_the_new_username()
    {
        var cut = RenderDetail();
        Client.Setup(c => c.RenameUserAsync(AliceId, "alice.renamed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RenameUsernameResponse(AliceId, "alice.renamed", "assigned"));
        Client.Setup(c => c.GetUserAsync(AliceId, It.IsAny<CancellationToken>())).ReturnsAsync(Alice("alice.renamed"));

        Field(cut).Input("alice.renamed");
        RenameForm(cut).Submit();

        cut.WaitForAssertion(() => VisibleText(cut.Find(".ui-page-header .ui-user-ref--rich")).Should().Be("Alice Example (alice.renamed)"));
        Client.Verify(c => c.RenameUserAsync(AliceId, "alice.renamed", It.IsAny<CancellationToken>()), Times.Once);
        Toasts.Toasts.Should().Contain(t => t.Message.Contains("Username changed"));
    }

    [Fact]
    public async Task While_the_server_has_not_confirmed_the_page_stays_visible_unchanged_and_the_form_is_busy_and_cannot_be_resubmitted()
    {
        var cut = RenderDetail();
        var gate = new TaskCompletionSource<RenameUsernameResponse>();
        Client.Setup(c => c.RenameUserAsync(AliceId, "alice.renamed", It.IsAny<CancellationToken>())).Returns(gate.Task);

        Field(cut).Input("alice.renamed");
        RenameForm(cut).Submit();

        cut.WaitForAssertion(() => RenameForm(cut).GetAttribute("aria-busy").Should().Be("true"));
        Field(cut).HasAttribute("disabled").Should().BeTrue("the field is locked while the change is pending");
        RenameForm(cut).TextContent.Should().Contain("Renaming");
        VisibleText(cut.Find(".ui-page-header .ui-user-ref--rich")).Should().Be("Alice Example (alice)", "nothing is changed optimistically");
        cut.FindAll(".ui-metadata-grid").Should().NotBeEmpty("content stays visible while pending");

        RenameForm(cut).Submit();
        Client.Verify(c => c.RenameUserAsync(AliceId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        Client.Setup(c => c.GetUserAsync(AliceId, It.IsAny<CancellationToken>())).ReturnsAsync(Alice("alice.renamed"));
        gate.SetResult(new RenameUsernameResponse(AliceId, "alice.renamed", "assigned"));
        cut.WaitForAssertion(() => RenameForm(cut).HasAttribute("aria-busy").Should().BeFalse());
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "username_taken", "That username belongs, or once belonged, to another user.")]
    [InlineData(HttpStatusCode.BadRequest, "username_reserved", "That username is reserved.")]
    [InlineData(HttpStatusCode.Conflict, "username_concurrent_change", "The user was changed by another operation. Reload and try again.")]
    public void A_server_refusal_is_shown_beside_the_field_with_the_typed_value_kept_and_the_old_name_unchanged(
        HttpStatusCode status, string code, string message)
    {
        var cut = RenderDetail();
        Client.Setup(c => c.RenameUserAsync(AliceId, "bob.taken", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UsernameRenameException(message, status, code));

        Field(cut).Input("bob.taken");
        RenameForm(cut).Submit();

        cut.WaitForAssertion(() => cut.Find("#rename-username-hint").TextContent.Should().Contain(message));
        Field(cut).GetAttribute("aria-invalid").Should().Be("true");
        Field(cut).GetAttribute("value").Should().Be("bob.taken");
        Field(cut).HasAttribute("disabled").Should().BeFalse("the operator can correct the value");
        VisibleText(cut.Find(".ui-page-header .ui-user-ref--rich")).Should().Be("Alice Example (alice)");
    }

    [Fact]
    public void Editing_after_a_refusal_clears_the_server_message()
    {
        var cut = RenderDetail();
        Client.Setup(c => c.RenameUserAsync(AliceId, "bob.taken", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UsernameRenameException("That username belongs, or once belonged, to another user.", HttpStatusCode.Conflict, "username_taken"));
        Field(cut).Input("bob.taken");
        RenameForm(cut).Submit();
        cut.WaitForAssertion(() => cut.Find("#rename-username-hint").TextContent.Should().Contain("another user"));

        Field(cut).Input("bob.free");

        cut.Find("#rename-username-hint").TextContent.Should().NotContain("another user");
    }

    [Fact]
    public void An_invalid_name_is_explained_and_never_sent()
    {
        var cut = RenderDetail();

        Field(cut).Input("Bad Name!");
        RenameForm(cut).Submit();

        cut.Find("#rename-username-hint").TextContent.Should().Contain("3 to 64 characters");
        Client.Verify(c => c.RenameUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Submitting_the_current_username_is_a_no_op_that_says_so()
    {
        var cut = RenderDetail();

        RenameForm(cut).Submit();

        cut.Find("#rename-username-hint").TextContent.Should().Contain("already");
        Client.Verify(c => c.RenameUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void A_forbidden_rename_is_reported_without_changing_anything()
    {
        var cut = RenderDetail();
        Client.Setup(c => c.RenameUserAsync(AliceId, "alice.renamed", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UsernameRenameException("Not authorized to manage users.", HttpStatusCode.Forbidden, null));

        Field(cut).Input("alice.renamed");
        RenameForm(cut).Submit();

        cut.WaitForAssertion(() => cut.Find("#rename-username-hint").TextContent.Should().Contain("Not authorized"));
        VisibleText(cut.Find(".ui-page-header .ui-user-ref--rich")).Should().Be("Alice Example (alice)");
    }
}
