namespace Apperture.Management.Ui.Shared.Models;

/// <summary>
/// A menu item within a domain (replaces SubMenuItem from IntegrationCenter).
/// </summary>
public class DomainMenuItem
{
    public string Text { get; set; } = string.Empty;
    public string? Href { get; set; }
    public string? Icon { get; set; }
    public List<DomainMenuItem>? Children { get; set; }
}
