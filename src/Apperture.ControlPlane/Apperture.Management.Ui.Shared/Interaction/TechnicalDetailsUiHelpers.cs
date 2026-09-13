namespace Apperture.Management.Ui.Shared;

/// <summary>
/// UIX-6 — conventions for identifier / metadata disclosure.
/// Technical details are always secondary: never the only place a required fact appears.
/// </summary>
public static class TechnicalDetailsUiHelpers
{
    public const string DefaultSummary = "Technical details";

    /// <summary>Placeholder for an absent identifier (never render an empty definition).</summary>
    public const string EmptyValue = "—";

    public static string ResolveSummary(string? summary) =>
        string.IsNullOrWhiteSpace(summary) ? DefaultSummary : summary.Trim();

    /// <summary>Display text for an identifier value; blank becomes the empty placeholder.</summary>
    public static string FormatValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? EmptyValue : value.Trim();

    /// <summary>Label text normalized for the definition list (identifiers keep their casing).</summary>
    public static string FormatLabel(string? label) =>
        string.IsNullOrWhiteSpace(label) ? EmptyValue : label.Trim();
}
