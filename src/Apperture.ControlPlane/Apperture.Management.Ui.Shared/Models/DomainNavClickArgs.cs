namespace Apperture.Management.Ui.Shared.Models;

/// <summary>
/// Arguments for rail item click (link vs domain opener).
/// Replaces MenuItemClickArgs from IntegrationCenter.
/// </summary>
public class DomainNavClickArgs
{
    public string? Domain { get; set; }
    public string? Href { get; set; }
}
