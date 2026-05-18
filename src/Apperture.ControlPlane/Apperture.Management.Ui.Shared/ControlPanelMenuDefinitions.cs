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
        // Dashboard and Config are custom zone owners (logo/gear); they are excluded from the middle dynamic loop.
        return new Dictionary<string, DomainMenu>(StringComparer.OrdinalIgnoreCase)
        {
            [SectionOwnerConstants.Dashboard] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Dashboard home", Href = ShellSectionRoutes.DashboardHome },
                    new DomainMenuItem { Text = "Manage dashboards (deferred)", Href = "/dashboard/manage" },
                    new DomainMenuItem { Text = "Add dashboard page (deferred)", Href = "/dashboard/add" },
                    new DomainMenuItem { Text = "Delete dashboard page (deferred)", Href = "/dashboard/delete" },
                    new DomainMenuItem { Text = "Edit dashboard layout (deferred)", Href = "/dashboard/layout" }
                }
            },
            ["Operations"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Control Center", Href = "/control/center" },
                    new DomainMenuItem { Text = "Blocked Work", Href = "/control/blocked-work" },
                    new DomainMenuItem { Text = "Audit Timeline", Href = "/control/audit" },
                    new DomainMenuItem { Text = "Execute", Href = "/execute" },
                    new DomainMenuItem { Text = "Start Operation (Legacy)", Href = "/operations/start" },
                    new DomainMenuItem { Text = "Start Workflow (Legacy)", Href = "/workflows/start" },
                    new DomainMenuItem { Text = "API Explorer", Href = "/tools/explorer" },
                    new DomainMenuItem
                    {
                        Id = "tools-nested-nav-demo",
                        Text = "Nested nav demo",
                        Href = "/demo/nested-nav",
                        Children = new List<DomainMenuItem>
                        {
                            new DomainMenuItem
                            {
                                Id = "tools-nested-nav-l1",
                                Text = "Level 1",
                                Href = "/demo/nested-nav/level-1",
                                Children = new List<DomainMenuItem>
                                {
                                    new DomainMenuItem
                                    {
                                        Id = "tools-nested-nav-l2",
                                        Text = "Level 2",
                                        Href = "/demo/nested-nav/level-1/level-2",
                                        Children = new List<DomainMenuItem>
                                        {
                                            new DomainMenuItem
                                            {
                                                Id = "tools-nested-nav-l3",
                                                Text = "Level 3",
                                                Href = "/demo/nested-nav/level-1/level-2/level-3"
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    new DomainMenuItem { Text = "Operational teams", Href = "/attention/operational-teams" },
                    new DomainMenuItem { Text = "Attention routing defaults", Href = "/attention/routing-defaults" }
                }
            },
            ["Automation"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "History", Href = "/runs" },
                    new DomainMenuItem { Text = "Scheduling", Href = "/schedules" }
                }
            },
            ["Integrations"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Integrations", Href = "/integrations" }
                }
            },
            ["Connections"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Connections", Href = "/connections" }
                }
            },
            [SectionOwnerConstants.Config] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Config", Href = ShellSectionRoutes.ConfigHome },
                    new DomainMenuItem
                    {
                        Text = "Authentication",
                        Href = "/config/auth",
                        Children = new List<DomainMenuItem>
                        {
                            new DomainMenuItem { Text = "Sign-in chooser", Href = "/config/auth/sign-in" },
                            new DomainMenuItem { Text = "Entra registrations", Href = "/config/auth/tenants" }
                        }
                    },
                    new DomainMenuItem
                    {
                        Text = "Modules",
                        Href = "/modules",
                        Children = new List<DomainMenuItem>
                        {
                            new DomainMenuItem { Text = "Installed Modules", Href = "/modules" }
                        }
                    },
                    new DomainMenuItem
                    {
                        Text = "Governance",
                        Href = "/config/governance",
                        Children = new List<DomainMenuItem>
                        {
                            new DomainMenuItem { Text = "Permission Catalog", Href = "/config/governance/catalog" },
                            new DomainMenuItem { Text = "Roles", Href = "/config/governance/roles" },
                            new DomainMenuItem { Text = "Users", Href = "/config/governance/users" }
                        }
                    },
                    new DomainMenuItem
                    {
                        Text = "Notifications",
                        Href = "/config/notifications",
                        Children = new List<DomainMenuItem>
                        {
                            new DomainMenuItem { Text = "Definitions", Href = "/config/notifications/definitions" },
                            new DomainMenuItem { Text = "Instances", Href = "/config/notifications/instances" },
                            new DomainMenuItem { Text = "Delivery", Href = "/config/notifications/delivery" },
                            new DomainMenuItem { Text = "Diagnostics", Href = "/config/notifications/diagnostics" }
                        }
                    }
                }
            }
        };
    }
}

