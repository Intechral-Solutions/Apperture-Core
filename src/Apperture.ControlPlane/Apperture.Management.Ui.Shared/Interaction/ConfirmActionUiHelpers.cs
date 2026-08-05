namespace Apperture.Management.Ui.Shared;

/// <summary>UIX-6 — consequence level of a confirmed action.</summary>
public enum ConfirmActionEmphasis
{
    /// <summary>Reversible or low-consequence change.</summary>
    Standard,

    /// <summary>Consequential but recoverable (clears an override, retires a resource).</summary>
    Caution,

    /// <summary>Removes or permanently changes platform configuration.</summary>
    Destructive,
}

/// <summary>UIX-6 — labels and styling for <c>ConfirmActionDialog</c>.</summary>
public static class ConfirmActionUiHelpers
{
    public const string DefaultConfirmLabel = "Confirm";
    public const string DefaultCancelLabel = "Cancel";
    public const string BusyLabelSuffix = "…";

    /// <summary>Confirm button classes; destructive uses the shared danger treatment.</summary>
    public static string ConfirmButtonClass(ConfirmActionEmphasis emphasis) => emphasis switch
    {
        ConfirmActionEmphasis.Destructive => "ui-toolbar-btn ui-toolbar-btn--danger",
        ConfirmActionEmphasis.Caution => "ui-toolbar-btn ui-dialog__confirm--caution",
        _ => "ui-toolbar-btn",
    };

    public static string ResolveConfirmLabel(string? confirmLabel) =>
        string.IsNullOrWhiteSpace(confirmLabel) ? DefaultConfirmLabel : confirmLabel.Trim();

    public static string ResolveCancelLabel(string? cancelLabel) =>
        string.IsNullOrWhiteSpace(cancelLabel) ? DefaultCancelLabel : cancelLabel.Trim();

    /// <summary>Label while the confirm handler is running (prevents duplicate submission ambiguity).</summary>
    public static string ResolveBusyLabel(string? confirmLabel) =>
        ResolveConfirmLabel(confirmLabel) + BusyLabelSuffix;

    /// <summary>
    /// Caution and destructive actions must state the consequence; a bare title is not enough.
    /// </summary>
    public static bool RequiresConsequenceText(ConfirmActionEmphasis emphasis) =>
        emphasis is ConfirmActionEmphasis.Caution or ConfirmActionEmphasis.Destructive;
}
