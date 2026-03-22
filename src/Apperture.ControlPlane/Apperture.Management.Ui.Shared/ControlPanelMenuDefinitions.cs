using Apperture.Management.Ui.Shared.Models;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Central menu definition for the Control Panel sidebar.
/// Used by both the server menu endpoint and the client fallback.
/// </summary>
public static class ControlPanelMenuDefinitions
{
    public static Dictionary<string, DomainMenu> GetDefaultMenus()
    {
        // Note: top-level keys are the "domain" names used by the sidebar.
        return new Dictionary<string, DomainMenu>(StringComparer.OrdinalIgnoreCase)
        {
            ["Runs"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Runs Console", Href = "/runs" },
                    new DomainMenuItem { Text = "Start Operation", Href = "/operations/start" },
                    new DomainMenuItem { Text = "Start Workflow", Href = "/workflows/start" },
                }
            },
            ["Control"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Control Center", Href = "/control/center" },
                    new DomainMenuItem { Text = "Blocked Work", Href = "/control/blocked-work" },
                    new DomainMenuItem { Text = "Audit Timeline", Href = "/control/audit" }
                }
            },
            ["Modules"] = new DomainMenu
            {
                Title = null,
                Items = { new DomainMenuItem { Text = "View All", Href = "/modules" } }
            },
            ["Test Suite"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    // Reuse existing workflow execution pages for now.
                    // When new test-related pages are added, extend this in one place.
                    new DomainMenuItem { Text = "Runs", Href = "/runs" },
                    new DomainMenuItem { Text = "Start Operation", Href = "/operations/start" },
                    new DomainMenuItem { Text = "Start Workflow", Href = "/workflows/start" },
                }
            },
            ["Catalog"] = new DomainMenu
            {
                Title = null,
                Items = { new DomainMenuItem { Text = "Browse Capabilities", Href = "/catalog" } }
            },
            ["Integrations"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "View All", Href = "/integrations" },
                    new DomainMenuItem { Text = "Create Integration", Href = "/integrations/new" }
                }
            },
            ["Environments"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "View All", Href = "/environments" },
                    new DomainMenuItem { Text = "Settings", Href = "/environments/settings" }
                }
            },
            ["Connections"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Active Connections", Href = "/connections" },
                    new DomainMenuItem { Text = "Add Connection", Href = "/connections/new" }
                }
            },
            ["Clients"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "All Clients", Href = "/clients" },
                    new DomainMenuItem { Text = "Create Client", Href = "/clients/new" }
                }
            },
            ["Schedules"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "View All", Href = "/schedules" },
                    new DomainMenuItem { Text = "Create Schedule", Href = "/schedules/new" }
                }
            },
            ["Tools"] = new DomainMenu
            {
                Title = null,
                Items = { new DomainMenuItem { Text = "API Explorer", Href = "/tools/explorer" } }
            },
            ["Governance"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Permission Catalog", Href = "/governance/catalog" },
                    new DomainMenuItem { Text = "Roles", Href = "/governance/roles" },
                    new DomainMenuItem { Text = "Users", Href = "/governance/users" }
                }
            }
        };
    }
}

