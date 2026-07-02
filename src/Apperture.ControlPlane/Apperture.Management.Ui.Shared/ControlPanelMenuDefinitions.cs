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
                    new DomainMenuItem { Text = "Dashboard home", Href = ShellSectionRoutes.DashboardHome }
                }
            },
            ["Operations"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Control Center", Href = "/control/center" },
                    new DomainMenuItem { Text = "Blocked Work", Href = "/control/blocked-work" },
                    new DomainMenuItem { Text = "Execute", Href = "/execute" },
                    new DomainMenuItem { Text = "Audit", Href = "/control/audit" }
                }
            },
            ["Automation"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Runs", Href = "/runs" },
                    new DomainMenuItem { Text = "Schedules", Href = "/schedules" }
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
                    new DomainMenuItem { Text = "Overview", Href = ShellSectionRoutes.ConfigHome },
                    new DomainMenuItem
                    {
                        Text = "Authentication",
                        Href = "/config/auth",
                        Children = new List<DomainMenuItem>
                        {
                            new DomainMenuItem { Text = "Sign-in experience", Href = "/config/auth/sign-in" },
                            new DomainMenuItem { Text = "Entra tenants", Href = "/config/auth/tenants" }
                        }
                    },
                    new DomainMenuItem
                    {
                        Text = "Governance",
                        Href = "/config/governance",
                        Children = new List<DomainMenuItem>
                        {
                            new DomainMenuItem { Text = "Permission catalog", Href = "/config/governance/catalog" },
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
                            new DomainMenuItem { Text = "Delivery", Href = "/config/notifications/delivery" }
                        }
                    },
                    new DomainMenuItem
                    {
                        Id = "config-attention",
                        Text = "Attention",
                        Href = "/attention/operational-teams",
                        Children = new List<DomainMenuItem>
                        {
                            new DomainMenuItem { Text = "Operational teams", Href = "/attention/operational-teams" },
                            new DomainMenuItem { Text = "Routing defaults", Href = "/attention/routing-defaults" },
                            new DomainMenuItem { Text = "Managed contacts", Href = "/config/contacts" }
                        }
                    },
                    new DomainMenuItem
                    {
                        Id = "config-platform-maintenance",
                        Text = "Platform maintenance",
                        Href = "/config/platform-settings",
                        Children = new List<DomainMenuItem>
                        {
                            new DomainMenuItem { Text = "Platform settings", Href = "/config/platform-settings" },
                            new DomainMenuItem { Text = "Modules", Href = "/modules" }
                        }
                    },
                    new DomainMenuItem
                    {
                        Id = "config-developer",
                        Text = "Developer & internal",
                        Href = ShellSectionRoutes.DeveloperTasks
                    }
                }
            }
        };
    }
}

