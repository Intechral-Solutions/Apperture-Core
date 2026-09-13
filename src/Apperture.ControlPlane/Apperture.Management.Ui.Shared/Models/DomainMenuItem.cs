namespace Apperture.Management.Ui.Shared.Models;

/// <summary>
/// A menu item within a domain (replaces SubMenuItem from IntegrationCenter).
/// </summary>
public class DomainMenuItem
{
    /// <summary>
    /// Optional stable key for expand/collapse state and client deduplication when <see cref="Text"/> repeats.
    /// Menu JSON from APIs may set this; if omitted, the shell derives a key from domain name + index path.
    /// </summary>
    public string? Id { get; set; }

    public string Text { get; set; } = string.Empty;
    public string? Href { get; set; }
    public string? Icon { get; set; }

    /// <summary>
    /// Optional <see cref="DomainMenuCapabilityTokens"/> value. When set, the menu item is omitted
    /// if the operator lacks that capability. Page and API authorization remain independent.
    /// </summary>
    public string? RequiredCapability { get; set; }

    public List<DomainMenuItem>? Children { get; set; }
}
