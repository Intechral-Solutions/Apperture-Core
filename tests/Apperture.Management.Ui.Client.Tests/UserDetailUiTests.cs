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

    // ---------- Rename failures: the operator is told, and the pending state is always released ----------

    private static UsernameRenameException ServerError(HttpStatusCode status) =>
        new("The server could not complete the request. " + UsernameRenameException.ConfirmByRefreshing, status, null);

    /// <summary>Every way a rename can end with the result unknown: the server may have committed the change.</summary>
    public static TheoryData<Exception, string> UnconfirmedFailures => new()
    {
        { new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException()), "did not answer in time" },
        { new HttpRequestException("Connection refused"), "connection was lost" },
        { new System.Text.Json.JsonException("The response is not JSON."), "could not be read" },
        { new NotSupportedException("The content type is not supported."), "could not be read" },
        { new InvalidOperationException("Rename succeeded without a result."), "could not be read" },
        { ServerError(HttpStatusCode.InternalServerError), "could not complete the request" },
        { ServerError(HttpStatusCode.BadGateway), "could not complete the request" },
        { ServerError(HttpStatusCode.GatewayTimeout), "could not complete the request" },
        { ServerError(HttpStatusCode.RequestTimeout), "could not complete the request" },
    };

    [Theory]
    [MemberData(nameof(UnconfirmedFailures))]
    public void An_unconfirmed_rename_explains_itself_releases_the_pending_state_and_can_be_retried(Exception failure, string expected)
    {
        var cut = RenderDetail();
        Client.Setup(c => c.RenameUserAsync(AliceId, "alice.renamed", It.IsAny<CancellationToken>())).ThrowsAsync(failure);

        Field(cut).Input("alice.renamed");
        RenameForm(cut).Submit();

        cut.WaitForAssertion(() => cut.Find("#rename-username-hint").TextContent.Should().Contain(expected));

        // The server may have committed the rename, so the page never says it failed or was not applied: it says the result
        // is unknown and sends the operator to refresh to confirm before trying again.
        var hint = cut.Find("#rename-username-hint").TextContent;
        hint.Should().Contain("may or may not have changed").And.Contain("Refresh this page to confirm");
        hint.Should().NotContainAny("was not changed", "Could not change", "failed", "Try again.", "not applied");

        RenameForm(cut).HasAttribute("aria-busy").Should().BeFalse("the pending state is released");
        Field(cut).HasAttribute("disabled").Should().BeFalse();
        Field(cut).GetAttribute("value").Should().Be("alice.renamed", "the typed value is kept");
        RenameButton(cut).TextContent.Should().Contain("Change username").And.NotContain("Renaming");
        RenameButton(cut).HasAttribute("disabled").Should().BeFalse("the button is usable again");
        VisibleText(cut.Find(".ui-page-header .ui-user-ref--rich")).Should().Be("Alice Example (alice)", "nothing changed on the page");

        RenameForm(cut).Submit();
        Client.Verify(c => c.RenameUserAsync(AliceId, "alice.renamed", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void An_unexpected_failure_still_releases_the_pending_state()
    {
        var cut = RenderDetail();
        Client.Setup(c => c.RenameUserAsync(AliceId, "alice.renamed", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("a defect, not an expected failure"));

        Field(cut).Input("alice.renamed");

        // A defect is not swallowed into a friendly message: the framework reports it (bUnit rethrows it from the event).
        var act = () => RenameForm(cut).Submit();
        act.Should().Throw<ArgumentException>().WithMessage("*a defect*");

        // What matters is that the form is not left locked behind it.
        RenameForm(cut).HasAttribute("aria-busy").Should().BeFalse();
        Field(cut).HasAttribute("disabled").Should().BeFalse();
        RenameButton(cut).TextContent.Should().Contain("Change username").And.NotContain("Renaming");
    }

    [Fact]
    public void A_reload_that_fails_after_a_confirmed_rename_is_not_reported_as_a_failed_rename()
    {
        var cut = RenderDetail();
        Client.Setup(c => c.RenameUserAsync(AliceId, "alice.renamed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RenameUsernameResponse(AliceId, "alice.renamed", "assigned"));
        Client.Setup(c => c.GetUserAsync(AliceId, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("reload failed"));

        Field(cut).Input("alice.renamed");
        RenameForm(cut).Submit();

        cut.WaitForAssertion(() => Toasts.Toasts.Should().Contain(t => t.Message.Contains("Username changed to alice.renamed")));
        RenameForm(cut).HasAttribute("aria-busy").Should().BeFalse();
        cut.FindAll("#rename-username-hint").Should().AllSatisfy(h => h.TextContent.Should().NotContain("not confirmed"));
    }

    // ---------- Responses that arrive after the operator has moved on ----------

    private static AngleSharp.Dom.IElement RenameButton(IRenderedComponent<UserDetail> cut) =>
        cut.Find("form[data-rename-username] button[type='submit']");

    private void StartAlicePendingRename(IRenderedComponent<UserDetail> cut, TaskCompletionSource<RenameUsernameResponse> gate)
    {
        Client.Setup(c => c.RenameUserAsync(AliceId, "alice.renamed", It.IsAny<CancellationToken>())).Returns(gate.Task);
        Field(cut).Input("alice.renamed");
        RenameForm(cut).Submit();
        cut.WaitForAssertion(() => RenameForm(cut).GetAttribute("aria-busy").Should().Be("true"));
    }

    private void NavigateToBob(IRenderedComponent<UserDetail> cut)
    {
        Client.Setup(c => c.GetUserAsync(BobId, It.IsAny<CancellationToken>())).ReturnsAsync(Bob());
        cut.Render(p => p.Add(c => c.UserId, BobId));
        cut.WaitForAssertion(() => VisibleText(cut.Find(".ui-page-header .ui-user-ref--rich")).Should().Be("Bob Builder (bob)"));
    }

    private static void AssertIdleRenameForm(IRenderedComponent<UserDetail> cut, string username)
    {
        RenameForm(cut).HasAttribute("aria-busy").Should().BeFalse("the form is not waiting on anything");
        Field(cut).HasAttribute("disabled").Should().BeFalse();
        Field(cut).GetAttribute("value").Should().Be(username);
        Field(cut).HasAttribute("aria-invalid").Should().BeFalse("no message from another user's rename");
        RenameButton(cut).TextContent.Should().Contain("Change username").And.NotContain("Renaming").And.NotContain("Username changed");
    }

    [Fact]
    public void Moving_to_another_user_gives_a_clean_idle_rename_form_even_while_the_previous_rename_is_pending()
    {
        var cut = RenderDetail();
        StartAlicePendingRename(cut, new TaskCompletionSource<RenameUsernameResponse>());

        NavigateToBob(cut);

        AssertIdleRenameForm(cut, "bob");
    }

    [Fact]
    public void Moving_to_another_user_clears_the_previous_users_rename_message()
    {
        var cut = RenderDetail();
        Client.Setup(c => c.RenameUserAsync(AliceId, "bob.taken", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UsernameRenameException("That username belongs, or once belonged, to another user.", HttpStatusCode.Conflict, "username_taken"));
        Field(cut).Input("bob.taken");
        RenameForm(cut).Submit();
        cut.WaitForAssertion(() => cut.Find("#rename-username-hint").TextContent.Should().Contain("another user"));

        NavigateToBob(cut);

        AssertIdleRenameForm(cut, "bob");
        cut.Find("#rename-username-hint").TextContent.Should().NotContain("another user");
    }

    [Fact]
    public void A_late_confirmation_for_the_previous_user_never_touches_the_form_of_the_user_now_shown()
    {
        var cut = RenderDetail();
        var gate = new TaskCompletionSource<RenameUsernameResponse>();
        StartAlicePendingRename(cut, gate);
        NavigateToBob(cut);

        gate.SetResult(new RenameUsernameResponse(AliceId, "alice.renamed", "assigned"));

        cut.WaitForAssertion(() => Toasts.Toasts.Should().Contain(t =>
            t.Message.Contains("Alice Example") && t.Message.Contains("alice.renamed")));
        AssertIdleRenameForm(cut, "bob");
        VisibleText(cut.Find(".ui-page-header .ui-user-ref--rich")).Should().Be("Bob Builder (bob)");
        Client.Verify(c => c.GetUserAsync(AliceId, It.IsAny<CancellationToken>()), Times.Once, "the previous user is not reloaded into this page");
        Client.Verify(c => c.GetUserAsync(BobId, It.IsAny<CancellationToken>()), Times.Once, "and the current user is not reloaded either");
    }

    [Fact]
    public void A_late_refusal_for_the_previous_user_is_reported_but_never_shown_on_the_form_of_the_user_now_shown()
    {
        var cut = RenderDetail();
        var gate = new TaskCompletionSource<RenameUsernameResponse>();
        StartAlicePendingRename(cut, gate);
        NavigateToBob(cut);

        gate.SetException(new UsernameRenameException(
            "That username belongs, or once belonged, to another user.", HttpStatusCode.Conflict, "username_taken"));

        // A confirmed refusal: the API said no, so "not changed" is true.
        cut.WaitForAssertion(() => Toasts.Toasts.Should().Contain(t =>
            t.Message.Contains("Alice Example") && t.Message.Contains("was not changed") && t.Message.Contains("another user")));
        AssertIdleRenameForm(cut, "bob");
        cut.Find("#rename-username-hint").TextContent.Should().NotContain("another user");
    }

    [Fact]
    public void A_late_timeout_for_the_previous_user_never_touches_the_form_and_does_not_claim_the_rename_failed()
    {
        var cut = RenderDetail();
        var gate = new TaskCompletionSource<RenameUsernameResponse>();
        StartAlicePendingRename(cut, gate);
        NavigateToBob(cut);

        gate.SetException(new TaskCanceledException("timeout", new TimeoutException()));

        cut.WaitForAssertion(() => Toasts.Toasts.Should().Contain(t => t.Message.Contains("Alice Example")));
        AssertIdleRenameForm(cut, "bob");
        AssertUnknownOutcomeToast();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public void A_late_server_or_gateway_error_for_the_previous_user_is_an_unknown_outcome_not_a_refusal(HttpStatusCode status)
    {
        var cut = RenderDetail();
        var gate = new TaskCompletionSource<RenameUsernameResponse>();
        StartAlicePendingRename(cut, gate);
        NavigateToBob(cut);

        gate.SetException(ServerError(status));

        cut.WaitForAssertion(() => Toasts.Toasts.Should().Contain(t => t.Message.Contains("Alice Example")));
        AssertIdleRenameForm(cut, "bob");
        AssertUnknownOutcomeToast();
    }

    [Fact]
    public void A_late_transport_failure_for_the_previous_user_is_an_unknown_outcome_not_a_refusal()
    {
        var cut = RenderDetail();
        var gate = new TaskCompletionSource<RenameUsernameResponse>();
        StartAlicePendingRename(cut, gate);
        NavigateToBob(cut);

        gate.SetException(new HttpRequestException("Connection reset"));

        cut.WaitForAssertion(() => Toasts.Toasts.Should().Contain(t => t.Message.Contains("Alice Example")));
        AssertIdleRenameForm(cut, "bob");
        AssertUnknownOutcomeToast();
    }

    /// <summary>A result that is not known is never reported as a failure or as "not changed": the server may have committed it.</summary>
    private void AssertUnknownOutcomeToast()
    {
        var toast = Toasts.Toasts.Should().ContainSingle(t => t.Message.Contains("Alice Example")).Which.Message;
        toast.Should().StartWith("Could not confirm the username change for Alice Example.");
        toast.Should().Contain("may or may not have changed").And.Contain("Refresh this page to confirm");
        toast.Should().NotContainAny("was not changed", "failed", "Try again.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_older_rename_finishing_never_clears_a_newer_renames_pending_state(bool olderFails)
    {
        var cut = RenderDetail();
        var older = new TaskCompletionSource<RenameUsernameResponse>();
        var newer = new TaskCompletionSource<RenameUsernameResponse>();
        StartAlicePendingRename(cut, older);
        NavigateToBob(cut);
        Client.Setup(c => c.RenameUserAsync(BobId, "bob.renamed", It.IsAny<CancellationToken>())).Returns(newer.Task);
        Field(cut).Input("bob.renamed");
        RenameForm(cut).Submit();
        cut.WaitForAssertion(() => RenameForm(cut).GetAttribute("aria-busy").Should().Be("true"));

        if (olderFails)
            older.SetException(new TaskCanceledException("timeout", new TimeoutException()));
        else
            older.SetResult(new RenameUsernameResponse(AliceId, "alice.renamed", "assigned"));
        cut.WaitForAssertion(() => Toasts.Toasts.Should().Contain(t => t.Message.Contains("Alice Example")));

        RenameForm(cut).GetAttribute("aria-busy").Should().Be("true", "the newer rename is still pending");
        Field(cut).HasAttribute("disabled").Should().BeTrue();
        RenameButton(cut).TextContent.Should().Contain("Renaming");
        RenameForm(cut).Submit();
        Client.Verify(c => c.RenameUserAsync(BobId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once, "still cannot be submitted twice");

        Client.Setup(c => c.GetUserAsync(BobId, It.IsAny<CancellationToken>())).ReturnsAsync(Bob("bob.renamed"));
        newer.SetResult(new RenameUsernameResponse(BobId, "bob.renamed", "assigned"));
        cut.WaitForAssertion(() => RenameForm(cut).HasAttribute("aria-busy").Should().BeFalse());
        VisibleText(cut.Find(".ui-page-header .ui-user-ref--rich")).Should().Be("Bob Builder (bob.renamed)");
    }

    [Fact]
    public async Task A_confirmation_arriving_after_the_page_is_gone_is_reported_without_touching_the_page()
    {
        var cut = RenderDetail();
        var gate = new TaskCompletionSource<RenameUsernameResponse>();
        StartAlicePendingRename(cut, gate);

        await DisposeComponentsAsync();
        gate.SetResult(new RenameUsernameResponse(AliceId, "alice.renamed", "assigned"));
        await FlushRendererQueueAsync();

        Toasts.Toasts.Should().Contain(t => t.Message.Contains("Alice Example") && t.Message.Contains("alice.renamed"));
        Client.Verify(c => c.GetUserAsync(AliceId, It.IsAny<CancellationToken>()), Times.Once, "a page that is gone is not reloaded");
    }

    /// <summary>
    /// The page's continuation after a request is queued on the renderer's synchronization context, so completing the request
    /// does not run it. Dispatching behind it and waiting is what proves it has run (an absence assertion needs this).
    /// </summary>
    private Task FlushRendererQueueAsync() => Renderer.Dispatcher.InvokeAsync(() => Task.CompletedTask);

    [Fact]
    public async Task A_cancellation_arriving_after_the_page_is_gone_is_ignored()
    {
        var cut = RenderDetail();
        var gate = new TaskCompletionSource<RenameUsernameResponse>();
        StartAlicePendingRename(cut, gate);

        await DisposeComponentsAsync();
        gate.SetCanceled();
        await FlushRendererQueueAsync();

        Toasts.Toasts.Should().BeEmpty("host teardown is not an outcome to announce");
        Client.Verify(c => c.GetUserAsync(AliceId, It.IsAny<CancellationToken>()), Times.Once);
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
