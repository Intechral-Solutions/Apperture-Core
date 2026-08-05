namespace Apperture.Management.Ui.Shared;

/// <summary>
/// UIX-6 — the Apperture interaction vocabulary. Each value maps to one supporting-surface
/// pattern documented in <c>docs/design-system/interaction-patterns.md</c>.
/// </summary>
public enum ManagementInteractionSurface
{
    /// <summary>Essential facts and actions rendered in page flow.</summary>
    Inline,

    /// <summary>Secondary content revealed in place (<c>Disclosure</c>).</summary>
    Disclosure,

    /// <summary>Identifiers, metadata, diagnostics (<c>TechnicalDetails</c>).</summary>
    TechnicalDetails,

    /// <summary>Short, noninteractive, nonessential explanation.</summary>
    Tooltip,

    /// <summary>Transient outcome that need not remain visible.</summary>
    Toast,

    /// <summary>Outcome tied to a control or section, remains visible.</summary>
    InlineFeedback,

    /// <summary>Persistent page-level state or guidance.</summary>
    PageMessage,

    /// <summary>Blocking, consequential decision or tightly scoped input.</summary>
    ModalDialog,

    /// <summary>Substantial, filtered, paged, shareable or reloadable work.</summary>
    DedicatedRoute,
}

/// <summary>
/// UIX-6 — shared class names and layering constants for interaction surfaces.
/// Styling lives in <c>management-ui-primitives.css</c>; features must not invent
/// local overlay classes or arbitrary z-index values.
/// </summary>
public static class ManagementInteractionSurfaces
{
    public const string DisclosureClass = "ui-disclosure";
    public const string DisclosureSummaryClass = "ui-disclosure__summary";
    public const string DisclosureBodyClass = "ui-disclosure__body";

    public const string TechnicalDetailsClass = "ui-technical-details";
    public const string TechnicalDetailsListClass = "ui-technical-details__list";
    public const string TechnicalDetailsLabelClass = "ui-technical-details__label";
    public const string TechnicalDetailsValueClass = "ui-technical-details__value";

    public const string DialogScrimClass = "ui-dialog-scrim";
    public const string DialogClass = "ui-dialog";
    public const string DialogHeaderClass = "ui-dialog__header";
    public const string DialogBodyClass = "ui-dialog__body";
    public const string DialogFooterClass = "ui-dialog__footer";

    /// <summary>Body class toggled while a modal owns scroll (set outside the Blazor root).</summary>
    public const string ModalOpenBodyClass = "apperture-modal-open";

    /// <summary>
    /// Documented layering model. Values live in CSS custom properties
    /// (<c>--z-*</c>); this table exists so tests and reviewers can assert ordering.
    /// </summary>
    public static IReadOnlyList<(string Layer, int ZIndex)> LayeringModel { get; } = new[]
    {
        ("page", 0),
        ("control-panel", 20),
        ("nav-rail", 40),
        ("dropdown", 50),
        ("shell-topbar", 60),
        ("dialog-scrim", 80),
        ("dialog", 81),
        ("toast", 100),
    };
}
