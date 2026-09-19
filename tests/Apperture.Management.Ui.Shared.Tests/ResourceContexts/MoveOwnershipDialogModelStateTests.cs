using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests.ResourceContexts;

/// <summary>
/// HSK-B Task 1: the additive dialog state on <see cref="MoveOwnershipDialogModel"/> -- the computed
/// <see cref="MoveDialogPhase"/>, <see cref="MoveSubmitErrorKind"/>, and the submit-time conflict path
/// (<see cref="MoveOwnershipDialogModel.ApplySubmitConflict"/>). The pre-existing behavior is pinned by
/// <c>MoveOwnershipDialogModelTests</c>, which is intentionally untouched.
/// </summary>
public sealed class MoveOwnershipDialogModelStateTests
{
    // ---- Phase -------------------------------------------------------------------------------------

    [Fact]
    public void Phase_is_NoTarget_for_a_fresh_model()
    {
        new MoveOwnershipDialogModel().Phase.Should().Be(MoveDialogPhase.NoTarget);
    }

    [Fact]
    public void Phase_is_NoTarget_after_the_selection_is_cleared_again()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.SelectTarget("");

        model.Phase.Should().Be(MoveDialogPhase.NoTarget);
    }

    [Fact]
    public void Phase_is_Loading_while_a_preview_is_in_flight()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.BeginPreview();

        model.Phase.Should().Be(MoveDialogPhase.Loading);
    }

    [Fact]
    public void Phase_is_Loading_when_a_target_is_selected_but_no_preview_has_started_yet()
    {
        // The window between @bind writing the selection and LoadPreviewAsync calling BeginPreview.
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");

        model.Phase.Should().Be(MoveDialogPhase.Loading);
    }

    [Fact]
    public void Phase_is_Failed_when_the_preview_failed()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.FailPreview(model.BeginPreview(), "The preview could not be loaded. Try again.");

        model.Phase.Should().Be(MoveDialogPhase.Failed);
    }

    [Fact]
    public void Phase_is_Allowed_for_a_CanMove_preview()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.ApplyPreview(model.BeginPreview(), Preview(canMove: true));

        model.Phase.Should().Be(MoveDialogPhase.Allowed);
    }

    [Fact]
    public void Phase_is_Blocked_for_a_preview_that_cannot_move()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.ApplyPreview(model.BeginPreview(), Preview(canMove: false));

        model.Phase.Should().Be(MoveDialogPhase.Blocked);
    }

    [Fact]
    public void Phase_stays_Allowed_when_a_submit_error_follows_a_valid_preview()
    {
        // A failed/busy Move keeps the preview on screen; the error is shown alongside, not instead.
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.ApplyPreview(model.BeginPreview(), Preview(canMove: true));

        model.SetSubmitError("Something failed.");

        model.Phase.Should().Be(MoveDialogPhase.Allowed);
    }

    [Fact]
    public void Phase_is_Loading_again_when_a_new_preview_begins_over_an_existing_one()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.ApplyPreview(model.BeginPreview(), Preview(canMove: true));

        model.BeginPreview();

        model.Phase.Should().Be(MoveDialogPhase.Loading);
    }

    [Fact]
    public void Phase_returns_to_NoTarget_after_Reset()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.ApplyPreview(model.BeginPreview(), Preview(canMove: false));

        model.Reset();

        model.Phase.Should().Be(MoveDialogPhase.NoTarget);
    }

    // ---- SubmitErrorKind ---------------------------------------------------------------------------

    [Fact]
    public void SubmitErrorKind_defaults_to_Failed()
    {
        new MoveOwnershipDialogModel().SubmitErrorKind.Should().Be(MoveSubmitErrorKind.Failed);
    }

    [Fact]
    public void SetSubmitError_with_one_argument_still_works_and_records_a_Failed_error()
    {
        var model = new MoveOwnershipDialogModel();

        model.SetSubmitError("boom");

        model.SubmitError.Should().Be("boom");
        model.SubmitErrorKind.Should().Be(MoveSubmitErrorKind.Failed);
    }

    [Fact]
    public void SetSubmitError_records_a_Busy_kind()
    {
        var model = new MoveOwnershipDialogModel();

        model.SetSubmitError("Another change is in progress.", MoveSubmitErrorKind.Busy);

        model.SubmitError.Should().Be("Another change is in progress.");
        model.SubmitErrorKind.Should().Be(MoveSubmitErrorKind.Busy);
    }

    [Fact]
    public void Clearing_the_submit_error_resets_the_kind_to_Failed()
    {
        var model = new MoveOwnershipDialogModel();
        model.SetSubmitError("busy", MoveSubmitErrorKind.Busy);

        model.SetSubmitError(null);

        model.SubmitError.Should().BeNull();
        model.SubmitErrorKind.Should().Be(MoveSubmitErrorKind.Failed);
    }

    [Fact]
    public void A_plain_error_after_a_Busy_error_is_Failed_not_sticky_Busy()
    {
        var model = new MoveOwnershipDialogModel();
        model.SetSubmitError("busy", MoveSubmitErrorKind.Busy);

        model.SetSubmitError("boom");

        model.SubmitErrorKind.Should().Be(MoveSubmitErrorKind.Failed);
    }

    [Fact]
    public void SelectTarget_resets_the_error_kind()
    {
        var model = new MoveOwnershipDialogModel();
        model.SetSubmitError("busy", MoveSubmitErrorKind.Busy);

        model.SelectTarget("app-canvas");

        model.SubmitError.Should().BeNull();
        model.SubmitErrorKind.Should().Be(MoveSubmitErrorKind.Failed);
    }

    [Fact]
    public void ApplyPreview_resets_the_error_kind()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        var generation = model.BeginPreview();
        model.SetSubmitError("busy", MoveSubmitErrorKind.Busy);

        model.ApplyPreview(generation, Preview(canMove: true));

        model.SubmitError.Should().BeNull();
        model.SubmitErrorKind.Should().Be(MoveSubmitErrorKind.Failed);
    }

    [Fact]
    public void Reset_resets_the_error_kind()
    {
        var model = new MoveOwnershipDialogModel();
        model.SetSubmitError("busy", MoveSubmitErrorKind.Busy);

        model.Reset();

        model.SubmitError.Should().BeNull();
        model.SubmitErrorKind.Should().Be(MoveSubmitErrorKind.Failed);
    }

    [Fact]
    public void CanSubmit_is_true_again_after_a_busy_error_with_a_valid_preview()
    {
        // Busy is transient: the preview is still valid, so the operator can press Move again.
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.ApplyPreview(model.BeginPreview(), Preview(canMove: true));
        model.BeginSubmit();
        model.SetSubmitError("busy", MoveSubmitErrorKind.Busy);
        model.EndSubmit();

        model.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void CanSubmit_stays_false_while_blocked()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.ApplyPreview(model.BeginPreview(), Preview(canMove: false));

        model.CanSubmit.Should().BeFalse();
    }

    // ---- ApplySubmitConflict / ChangedSincePreview -------------------------------------------------

    [Fact]
    public void ChangedSincePreview_is_false_by_default()
    {
        new MoveOwnershipDialogModel().ChangedSincePreview.Should().BeFalse();
    }

    [Fact]
    public void ApplySubmitConflict_swaps_in_the_blocked_preview_and_flags_the_change()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        var generation = model.BeginPreview();
        model.ApplyPreview(generation, Preview(canMove: true));
        model.BeginSubmit();
        var conflict = Preview(canMove: false);

        model.ApplySubmitConflict(model.CurrentGeneration, conflict);

        model.Preview.Should().BeSameAs(conflict);
        model.ChangedSincePreview.Should().BeTrue();
        model.IsLoadingPreview.Should().BeFalse();
        model.SubmitError.Should().BeNull();
        model.Phase.Should().Be(MoveDialogPhase.Blocked);
        model.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public void ApplySubmitConflict_clears_an_existing_submit_error()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.ApplyPreview(model.BeginPreview(), Preview(canMove: true));
        model.SetSubmitError("busy", MoveSubmitErrorKind.Busy);

        model.ApplySubmitConflict(model.CurrentGeneration, Preview(canMove: false));

        model.SubmitError.Should().BeNull();
        model.SubmitErrorKind.Should().Be(MoveSubmitErrorKind.Failed);
    }

    [Fact]
    public void ApplySubmitConflict_with_a_stale_generation_is_discarded()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        var first = model.BeginPreview();
        model.SelectTarget("app-campus");
        var second = model.BeginPreview();
        var current = Preview(canMove: true);
        model.ApplyPreview(second, current);

        model.ApplySubmitConflict(first, Preview(canMove: false)); // stale: superseded by `second`

        model.Preview.Should().BeSameAs(current);
        model.ChangedSincePreview.Should().BeFalse();
        model.Phase.Should().Be(MoveDialogPhase.Allowed);
        model.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void ChangedSincePreview_clears_on_SelectTarget()
    {
        var model = ModelWithConflict();

        model.SelectTarget("app-campus");

        model.ChangedSincePreview.Should().BeFalse();
    }

    [Fact]
    public void ChangedSincePreview_clears_on_BeginPreview()
    {
        var model = ModelWithConflict();

        model.BeginPreview();

        model.ChangedSincePreview.Should().BeFalse();
    }

    [Fact]
    public void ChangedSincePreview_clears_on_ApplyPreview()
    {
        var model = ModelWithConflict();
        var generation = model.BeginPreview();
        // BeginPreview already cleared it; re-flag it to prove ApplyPreview clears it on its own.
        model.ApplySubmitConflict(generation, Preview(canMove: false));
        model.ChangedSincePreview.Should().BeTrue();

        model.ApplyPreview(generation, Preview(canMove: true));

        model.ChangedSincePreview.Should().BeFalse();
    }

    [Fact]
    public void ChangedSincePreview_clears_on_Reset()
    {
        var model = ModelWithConflict();

        model.Reset();

        model.ChangedSincePreview.Should().BeFalse();
    }

    [Fact]
    public void A_stale_ApplyPreview_does_not_clear_ChangedSincePreview()
    {
        // ApplyPreview's stale-generation early return is unchanged: it must not touch any state.
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        var first = model.BeginPreview();
        var second = model.BeginPreview();
        model.ApplySubmitConflict(second, Preview(canMove: false));

        model.ApplyPreview(first, Preview(canMove: true));

        model.ChangedSincePreview.Should().BeTrue();
        model.Preview!.CanMove.Should().BeFalse();
    }

    private static MoveOwnershipDialogModel ModelWithConflict()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.ApplyPreview(model.BeginPreview(), Preview(canMove: true));
        model.ApplySubmitConflict(model.CurrentGeneration, Preview(canMove: false));
        model.ChangedSincePreview.Should().BeTrue("test precondition");
        return model;
    }

    private static ResourceMovePreviewDto Preview(bool canMove) =>
        new("Integration", "int-1", default!, default!, canMove, [], []);
}
