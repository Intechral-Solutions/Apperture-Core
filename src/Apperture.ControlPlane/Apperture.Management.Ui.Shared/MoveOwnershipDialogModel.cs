using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>One selectable move target: either the reserved Shared context or an active Application.</summary>
public sealed record MoveOwnershipTargetOption(string Id, string Name);

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
        SubmitError = null;
        IsLoadingPreview = false;
    }

    /// <summary>Starts a new preview request. Returns the generation to hand back to <see cref="ApplyPreview"/>/<see cref="FailPreview"/>.</summary>
    public int BeginPreview()
    {
        var generation = _previewGuard.Begin();
        CurrentGeneration = generation;
        IsLoadingPreview = true;
        return generation;
    }

    /// <summary>Applies a successfully fetched preview, unless a newer preview request has since started.</summary>
    public void ApplyPreview(int generation, ResourceMovePreviewDto preview)
    {
        if (!_previewGuard.IsCurrent(generation))
            return;

        Preview = preview;
        IsLoadingPreview = false;
        SubmitError = null;
    }

    /// <summary>Records a preview-fetch failure, unless a newer preview request has since started.</summary>
    public void FailPreview(int generation, string message)
    {
        if (!_previewGuard.IsCurrent(generation))
            return;

        IsLoadingPreview = false;
        SubmitError = message;
    }

    public void SetSubmitError(string? message) => SubmitError = message;

    /// <summary>Returns the dialog to its initial, no-selection state (e.g. on cancel).</summary>
    public void Reset()
    {
        TargetOptions = [];
        SelectedTargetId = "";
        IsLoadingPreview = false;
        Preview = null;
        SubmitError = null;
    }
}
