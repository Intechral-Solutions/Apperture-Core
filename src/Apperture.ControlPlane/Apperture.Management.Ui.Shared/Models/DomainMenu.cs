namespace Apperture.Management.Ui.Shared.Models;

/// <summary>
/// Menu structure for a domain (replaces SubMenu from IntegrationCenter).
/// </summary>
public class DomainMenu
{
    public string? Title { get; set; }
    public List<DomainMenuItem> Items { get; set; } = new();
}
