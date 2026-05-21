namespace Apperture.Management.Ui.Shared.Models;

/// <summary>
/// Shell top-bar orientation: parent breadcrumb links plus page title/subtitle (title is not in the trail).
/// </summary>
public sealed record ManagementShellOrientation(
    IReadOnlyList<BreadcrumbItem> Parents,
    string? Title,
    string? Subtitle)
{
    public static ManagementShellOrientation Empty { get; } = new([], null, null);
}
