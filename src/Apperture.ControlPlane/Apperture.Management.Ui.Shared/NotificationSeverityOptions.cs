namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Closed set of severity tokens offered by the built-in-definition "Platform defaults" control.
/// Local literal constants, matching <see cref="NotificationToastSeverityMapper"/>'s convention: neither
/// this project nor <c>Apperture.Management.Ui.Client</c> references
/// <c>Apperture.Core.Application.PlatformEvents</c> (where the canonical <c>PlatformEventSeverities</c>
/// constants live) directly or transitively, so the four tokens are mirrored here rather than reused via
/// a new cross-project dependency taken solely for four strings.
/// </summary>
public static class NotificationSeverityOptions
{
    public static readonly IReadOnlyList<(string Value, string Label)> All =
    [
        ("critical", "Critical"),
        ("error", "Error"),
        ("warning", "Warning"),
        ("info", "Info"),
    ];
}
