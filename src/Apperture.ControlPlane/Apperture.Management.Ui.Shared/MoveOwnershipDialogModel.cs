using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>One selectable move target: either the reserved Shared context or an active Application.</summary>
public sealed record MoveOwnershipTargetOption(string Id, string Name);

/// <summary>
/// HSK-B: which body the dialog should render. Computed by <see cref="MoveOwnershipDialogModel.Phase"/>
/// from the model's existing state -- it adds no state of its own.
/// </summary>
public enum MoveDialogPhase
{
    /// <summary>No target chosen yet.</summary>
    NoTarget,

    /// <summary>A target is chosen and its preview is loading (or about to start).</summary>
    Loading,

    /// <summary>The preview could not be loaded.</summary>
    Failed,

    /// <summary>The preview says the move is allowed.</summary>
    Allowed,

    /// <summary>The preview says the move is blocked.</summary>
    Blocked
}

/// <summary>HSK-B: distinguishes the transient busy-gate rejection from any other submit failure.</summary>
public enum MoveSubmitErrorKind
{
    /// <summary>Any ordinary failure.</summary>
    Failed,

    /// <summary>Another ownership operation held the gate; retrying shortly is expected to work.</summary>
    Busy
}

/// <summary>
/// CTX-3B Plan 5 (Task 4): pure decision logic behind <c>MoveOwnershipDialog.razor</c>. Owns the target
/// option list, the selected target, the current preview and submit error, and <see cref="CanSubmit"/>.
/// Performs no I/O -- the razor component calls <see cref="IManagementClient"/> and hands results back
/// in via <see cref="ApplyPreview"/>/<see cref="FailPreview"/>.
/// </summary>
/// <remarks>
/// Stale-response protection reuses <see cref="NavigationLoadGuard"/> -- the same generation-counter
/// technique <c>IntegrationDetail.razor</c>'s <c>LoadAsync</c> already uses to discard superseded async
/// responses -- rather than a second, hand-rolled counter field.
/// </remarks>
public sealed class MoveOwnershipDialogModel
{
    private readonly NavigationLoadGuard _previewGuard = new();

    public IReadOnlyList<MoveOwnershipTargetOption> TargetOptions { get; private set; } = [];

    public string SelectedTargetId { get; private set; } = "";

    public bool IsLoadingPreview { get; private set; }

    public ResourceMovePreviewDto? Preview { get; private set; }

    public string? SubmitError { get; private set; }

    /// <summary>
    /// Kind of the current <see cref="SubmitError"/>. Always <see cref="MoveSubmitErrorKind.Failed"/> when
    /// there is no error -- it is reset whenever the error is cleared.
    /// </summary>
    public MoveSubmitErrorKind SubmitErrorKind { get; private set; } = MoveSubmitErrorKind.Failed;

    /// <summary>
    /// True when the move endpoint answered with a fresh, blocked preview at submit time (the world changed
    /// between preview and Move; see <see cref="ApplySubmitConflict"/>). Cleared as soon as the operator
    /// picks another target or a new preview starts/applies.
    /// </summary>
    public bool ChangedSincePreview { get; private set; }

    /// <summary>
    /// Which body the dialog renders. Purely derived from existing state: no target selected is
    /// <see cref="MoveDialogPhase.NoTarget"/>; an in-flight preview -- or a selected target still awaiting
    /// its preview -- is <see cref="MoveDialogPhase.Loading"/>; a failed preview is
    /// <see cref="MoveDialogPhase.Failed"/>; otherwise the preview decides Allowed vs Blocked. A submit
    /// error alongside a valid preview does not change the phase (the preview stays on screen).
    /// </summary>
    public MoveDialogPhase Phase
    {
        get
        {
            if (string.IsNullOrEmpty(SelectedTargetId))
                return MoveDialogPhase.NoTarget;
            if (IsLoadingPreview)
                return MoveDialogPhase.Loading;
            if (Preview is null)
                return SubmitError is not null ? MoveDialogPhase.Failed : MoveDialogPhase.Loading;
            return Preview.CanMove ? MoveDialogPhase.Allowed : MoveDialogPhase.Blocked;
        }
    }

    /// <summary>
    /// True from <see cref="BeginSubmit"/> until <see cref="EndSubmit"/> -- the move request is in
    /// flight. Blocks <see cref="CanSubmit"/> (no double-submit via rapid clicks) and is combined with
    /// <see cref="IsLoadingPreview"/> by the razor component's <c>ManagementDialog.IsBusy</c> so
    /// Escape/scrim/Cancel are also blocked mid-request, mirroring <c>ConfirmActionDialog</c>'s
    /// <c>_busy</c> guarantee.
    /// </summary>
    public bool IsSubmitting { get; private set; }

    /// <summary>
    /// The generation of the most recently started preview request. Used by the razor component to
    /// force-apply a fresh preview-shaped result obtained outside <see cref="BeginPreview"/> (e.g. the
    /// move endpoint's own response when a concurrent change blocks the move at submit time) without
    /// starting a new generation.
    /// </summary>
    public int CurrentGeneration { get; private set; }

    /// <summary>
    /// True only when a target is selected, no preview is in flight, and the most recent preview both
    /// applies to the current selection and says the move is allowed. Never trusts a stale or absent
    /// preview.
    /// </summary>
    public bool CanSubmit =>
        !string.IsNullOrEmpty(SelectedTargetId)
        && !IsLoadingPreview
        && !IsSubmitting
        && Preview is { CanMove: true };

    /// <summary>
    /// Builds the target option list: Shared (if known) followed by the active Applications, excluding
    /// whichever one is the resource's current owner. Callers pass <c>ResourceContextViewState</c>'s
    /// already-active-only, already-sorted lists verbatim -- this does not re-filter or re-sort them.
    /// </summary>
    public static IReadOnlyList<MoveOwnershipTargetOption> BuildTargetOptions(
        ResourceContextDto? shared,
        IReadOnlyList<ResourceContextDto> activeApplications,
        string? currentResourceContextId)
    {
        var options = new List<MoveOwnershipTargetOption>();

        if (shared is not null && !string.Equals(shared.Id, currentResourceContextId, StringComparison.Ordinal))
            options.Add(new MoveOwnershipTargetOption(shared.Id, shared.Name));

        foreach (var application in activeApplications)
        {
            if (string.Equals(application.Id, currentResourceContextId, StringComparison.Ordinal))
                continue;

            options.Add(new MoveOwnershipTargetOption(application.Id, application.Name));
        }

        return options;
    }

    public void SetTargetOptions(
        ResourceContextDto? shared,
        IReadOnlyList<ResourceContextDto> activeApplications,
        string? currentResourceContextId) =>
        TargetOptions = BuildTargetOptions(shared, activeApplications, currentResourceContextId);

    /// <summary>Selects a target and clears any preview/error from a previously selected target.</summary>
    public void SelectTarget(string targetId)
    {
        SelectedTargetId = targetId;
        Preview = null;
        ClearSubmitError();
        ChangedSincePreview = false;
        IsLoadingPreview = false;
    }

    /// <summary>Starts a new preview request. Returns the generation to hand back to <see cref="ApplyPreview"/>/<see cref="FailPreview"/>.</summary>
    public int BeginPreview()
    {
        var generation = _previewGuard.Begin();
        CurrentGeneration = generation;
        IsLoadingPreview = true;
        ChangedSincePreview = false;
        return generation;
    }

    /// <summary>Applies a successfully fetched preview, unless a newer preview request has since started.</summary>
    public void ApplyPreview(int generation, ResourceMovePreviewDto preview)
    {
        if (!_previewGuard.IsCurrent(generation))
            return;

        Preview = preview;
        IsLoadingPreview = false;
        ClearSubmitError();
        ChangedSincePreview = false;
    }

    /// <summary>
    /// Applies the fresh, preview-shaped result the move endpoint returned when a concurrent change blocked
    /// the move at submit time, and flags it as <see cref="ChangedSincePreview"/> so the dialog can say so.
    /// Guarded by the same generation as <see cref="ApplyPreview"/>: a result belonging to a superseded
    /// request (the operator has since picked another target) is discarded.
    /// </summary>
    public void ApplySubmitConflict(int generation, ResourceMovePreviewDto result)
    {
        if (!_previewGuard.IsCurrent(generation))
            return;

        Preview = result;
        ChangedSincePreview = true;
        IsLoadingPreview = false;
        ClearSubmitError();
    }

    /// <summary>Records a preview-fetch failure, unless a newer preview request has since started.</summary>
    public void FailPreview(int generation, string message)
    {
        if (!_previewGuard.IsCurrent(generation))
            return;

        IsLoadingPreview = false;
        SubmitError = message;
    }

    /// <summary>
    /// Sets or clears the submit error. Passing <c>null</c> clears it and resets the kind to
    /// <see cref="MoveSubmitErrorKind.Failed"/>.
    /// </summary>
    public void SetSubmitError(string? message, MoveSubmitErrorKind kind = MoveSubmitErrorKind.Failed)
    {
        SubmitError = message;
        SubmitErrorKind = message is null ? MoveSubmitErrorKind.Failed : kind;
    }

    private void ClearSubmitError()
    {
        SubmitError = null;
        SubmitErrorKind = MoveSubmitErrorKind.Failed;
    }

    /// <summary>Marks a move request as in flight. Pair with <see cref="EndSubmit"/> (e.g. in a <c>finally</c>).</summary>
    public void BeginSubmit() => IsSubmitting = true;

    /// <summary>Clears the in-flight flag set by <see cref="BeginSubmit"/>.</summary>
    public void EndSubmit() => IsSubmitting = false;

    /// <summary>
    /// Returns the dialog to its initial, no-selection state (e.g. on cancel or after a successful move).
    /// Also starts a new generation, so a preview response still in flight when the dialog closed is
    /// discarded by <see cref="ApplyPreview"/>/<see cref="ApplySubmitConflict"/>/<see cref="FailPreview"/>
    /// instead of resurfacing as a stale preview on the next open. (<c>IsBusy</c> only blocks closing
    /// during a submit, not during a preview load, so this race is reachable.)
    /// </summary>
    public void Reset()
    {
        CurrentGeneration = _previewGuard.Begin();
        TargetOptions = [];
        SelectedTargetId = "";
        IsLoadingPreview = false;
        IsSubmitting = false;
        Preview = null;
        ClearSubmitError();
        ChangedSincePreview = false;
    }
}
