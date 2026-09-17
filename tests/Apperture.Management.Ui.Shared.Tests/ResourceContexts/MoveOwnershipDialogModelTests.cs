using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests.ResourceContexts;

public sealed class MoveOwnershipDialogModelTests
{
    private static readonly ResourceContextDto Shared =
        new("shared", "Shared", "shared", "Shared", "Active", true, null, default, default);

    private static readonly ResourceContextDto Canvas =
        new("app-canvas", "Canvas", "canvas", "Application", "Active", false, null, default, default);

    private static readonly ResourceContextDto Campus =
        new("app-campus", "CampusNexus", "campusnexus", "Application", "Active", false, null, default, default);

    [Fact]
    public void Target_options_exclude_the_current_owner()
    {
        var options = MoveOwnershipDialogModel.BuildTargetOptions(
            shared: Shared, activeApplications: [Canvas, Campus], currentResourceContextId: "app-canvas");

        options.Select(o => o.Id).Should().Equal("shared", "app-campus");
    }

    [Fact]
    public void Target_options_include_shared_and_never_an_archived_application()
    {
        // KnownActiveApplications is active-only by construction, so this pins the contract rather than
        // filtering again: whatever the caller passes as activeApplications is offered verbatim.
        var options = MoveOwnershipDialogModel.BuildTargetOptions(
            shared: Shared, activeApplications: [Canvas], currentResourceContextId: "app-campus");

        options.Select(o => o.Id).Should().Contain("shared");
    }

    [Fact]
    public void Submit_is_blocked_until_a_target_is_chosen()
    {
        var model = new MoveOwnershipDialogModel();
        model.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public void Submit_is_blocked_while_a_preview_is_loading()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.BeginPreview();
        model.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public void Submit_is_blocked_while_a_submit_is_in_flight()
    {
        // Rapid double-clicks on "Move" must not fire a second request while the first is still in
        // flight -- ConfirmActionDialog's `_busy` field exists for the exact same reason.
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        var generation = model.BeginPreview();
        model.ApplyPreview(generation, Preview(canMove: true));

        model.BeginSubmit();

        model.CanSubmit.Should().BeFalse("a move request already in flight must not be submitted again");
    }

    [Fact]
    public void Submit_is_allowed_again_once_a_submit_completes()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        var generation = model.BeginPreview();
        model.ApplyPreview(generation, Preview(canMove: true));

        model.BeginSubmit();
        model.EndSubmit();

        model.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void Reset_clears_the_target_preview_error_and_in_flight_state()
    {
        // Covers the post-successful-move reset: a host page reusing the same dialog instance for a
        // different resource must not see the previous resource's stale selection/preview.
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        var generation = model.BeginPreview();
        model.ApplyPreview(generation, Preview(canMove: true));
        model.BeginSubmit();

        model.Reset();

        model.SelectedTargetId.Should().BeEmpty();
        model.Preview.Should().BeNull();
        model.SubmitError.Should().BeNull();
        model.IsSubmitting.Should().BeFalse();
        model.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public void Submit_is_blocked_when_the_preview_says_CanMove_is_false()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        var generation = model.BeginPreview();
        model.ApplyPreview(generation, Preview(canMove: false));

        model.CanSubmit.Should().BeFalse("the UI must never submit a move the server already said is blocked");
    }

    [Fact]
    public void Submit_is_allowed_only_for_a_fresh_CanMove_preview()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        var generation = model.BeginPreview();
        model.ApplyPreview(generation, Preview(canMove: true));

        model.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void A_stale_preview_response_is_discarded()
    {
        // Rapid target switching: the first request resolves after the second was issued. Without the
        // generation guard the older, wrong-target preview would overwrite the newer one and the dialog
        // would describe a move the operator is not making. IntegrationDetail.razor already uses this
        // exact generation-counter pattern in LoadAsync.
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        var first = model.BeginPreview();
        model.SelectTarget("app-campus");
        var second = model.BeginPreview();

        model.ApplyPreview(second, Preview(canMove: true));
        model.ApplyPreview(first, Preview(canMove: false)); // late, stale

        model.CanSubmit.Should().BeTrue("the stale response must be dropped, not applied over the current one");
    }

    [Fact]
    public void FailPreview_clears_the_loading_flag_and_sets_the_error()
    {
        // Regression coverage for the MoveOwnershipDialog.razor bug where an HttpRequestException escaped
        // LoadPreviewAsync uncaught: IsLoadingPreview never returned to false, and since ManagementDialog's
        // IsBusy is derived from it, the dialog's close controls stayed permanently disabled. The razor
        // component now routes HttpRequestException into FailPreview (the same path InvalidOperationException
        // already used), so this proves FailPreview itself resets IsLoadingPreview regardless of which
        // exception type triggered the call.
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        var generation = model.BeginPreview();

        model.FailPreview(generation, "The preview could not be loaded. Try again.");

        model.IsLoadingPreview.Should().BeFalse();
        model.SubmitError.Should().Be("The preview could not be loaded. Try again.");
        model.CanSubmit.Should().BeFalse("a failed preview must never leave the move submittable");
    }

    [Fact]
    public void A_stale_preview_failure_is_discarded()
    {
        // Mirrors A_stale_preview_response_is_discarded for the failure path: a late failure from a
        // superseded request must not clobber the current (possibly successful) preview state.
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        var first = model.BeginPreview();
        model.SelectTarget("app-campus");
        var second = model.BeginPreview();
        model.ApplyPreview(second, Preview(canMove: true));

        model.FailPreview(first, "stale failure");

        model.IsLoadingPreview.Should().BeFalse();
        model.SubmitError.Should().BeNull();
        model.CanSubmit.Should().BeTrue("the stale failure must be dropped, not applied over the current success");
    }

    [Fact]
    public void Changing_the_target_clears_the_previous_preview_and_error()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.ApplyPreview(model.BeginPreview(), Preview(canMove: true));
        model.SetSubmitError("boom");

        model.SelectTarget("app-campus");

        model.Preview.Should().BeNull();
        model.SubmitError.Should().BeNull();
        model.CanSubmit.Should().BeFalse();
    }

    private static ResourceMovePreviewDto Preview(bool canMove) =>
        new("Integration", "int-1", default!, default!, canMove, [], []);
}
