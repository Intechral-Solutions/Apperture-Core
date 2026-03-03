namespace Apperture.Management.Ui.Shared.Options;

/// <summary>
/// Options for the Control Panel menu data source (replaces MenuOptions from IntegrationCenter).
/// </summary>
public class ControlPanelMenuOptions
{
    public string JsonSource { get; set; } = "/api/menus";
}
