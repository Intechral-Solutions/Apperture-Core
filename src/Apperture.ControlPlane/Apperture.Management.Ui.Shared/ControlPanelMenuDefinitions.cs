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
        // Insertion order is middle-rail order after Control Center / Config are pulled out.
        return new Dictionary<string, DomainMenu>(StringComparer.OrdinalIgnoreCase)
        {
            [SectionOwnerConstants.ControlCenter] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Control Center", Href = ShellSectionRoutes.ControlCenterHome },
                    new DomainMenuItem { Text = "Runs", Href = "/runs" },
                    new DomainMenuItem { Text = "Execute", Href = "/execute" },
                    new DomainMenuItem
                    {
                        Text = "Audit",
                        Href = "/control/audit",
                        RequiredCapability = DomainMenuCapabilityTokens.ViewAudit
                    }
                }
            },
            [SectionOwnerConstants.Integrations] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem
                    {
                        Text = "Integrations",
                        Href = "/integrations",
                        RequiredCapability = DomainMenuCapabilityTokens.ViewIntegrations
                    }
                }
            },
            [SectionOwnerConstants.BusinessData] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Collections", Href = ShellSectionRoutes.BusinessDataHome },
                    new DomainMenuItem
                    {
                        Text = "Customers",
                        Href = BusinessDataUiRoutes.Customers,
                        RequiredCapability = DomainMenuCapabilityTokens.ViewIntegrations
                    }
                }
            },
            [SectionOwnerConstants.Config] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Overview", Href = ShellSectionRoutes.ConfigHome },
                    new DomainMenuItem { Text = "Inventory", Href = ShellSectionRoutes.Inventory },
                    new DomainMenuItem { Text = "Applications", Href = "/config/applications" },
                    new DomainMenuItem
                    {
                        Text = "Governance",
                        Href = "/config/governance",
                        RequiredCapability = DomainMenuCapabilityTokens.ViewGovernance,
                        Children = new List<DomainMenuItem>
                        {
                            new DomainMenuItem { Text = "Permission catalog", Href = "/config/governance/catalog" },
                            new DomainMenuItem { Text = "Roles", Href = "/config/governance/roles" },
                            new DomainMenuItem { Text = "Users", Href = "/config/governance/users" }
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
                            new DomainMenuItem
                            {
                                Text = "Managed contacts",
                                Href = "/config/contacts",
                                RequiredCapability = DomainMenuCapabilityTokens.ViewManagedContacts
                            },
                            new DomainMenuItem
                            {
                                Text = "Notifications",
                                Href = "/config/notifications",
                                RequiredCapability = DomainMenuCapabilityTokens.ViewNotificationDefinitions,
                                Children = new List<DomainMenuItem>
                                {
                                    new DomainMenuItem { Text = "Definitions", Href = "/config/notifications/definitions" },
                                    new DomainMenuItem { Text = "Instances", Href = "/config/notifications/instances" },
                                    new DomainMenuItem { Text = "Delivery", Href = "/config/notifications/delivery" },
                                    new DomainMenuItem { Text = "Diagnostics", Href = "/config/notifications/diagnostics" }
                                }
                            }
                        }
                    },
                    new DomainMenuItem
                    {
                        Id = "config-system",
                        Text = "System",
                        Href = "/config/platform-settings",
                        Children = new List<DomainMenuItem>
                        {
                            new DomainMenuItem
                            {
                                Text = "Platform settings",
                                Href = "/config/platform-settings",
                                RequiredCapability = DomainMenuCapabilityTokens.ViewPlatformSettings
                            },
                            // Structural parent: deliberately NO RequiredCapability. The menu filter drops a gated
                            // parent together with its whole subtree, which would hide an authorized Modules child
                            // from an operator who can manage modules but read neither Component family. The parent
                            // is kept whenever at least one child survives, and its own Href (shared with Catalog) is
                            // nulled when Catalog is filtered out.
                            new DomainMenuItem
                            {
                                Id = "config-components",
                                Text = "Components",
                                Href = "/components",
                                Children = new List<DomainMenuItem>
                                {
                                    new DomainMenuItem
                                    {
                                        Text = "Catalog",
                                        Href = "/components",
                                        RequiredCapability = DomainMenuCapabilityTokens.ViewComponents
                                    },
                                    new DomainMenuItem
                                    {
                                        Text = "Modules",
                                        Href = "/modules",
                                        RequiredCapability = DomainMenuCapabilityTokens.ManageModules
                                    }
                                }
                            },
                            new DomainMenuItem
                            {
                                Text = "Authentication",
                                Href = "/config/auth",
                                RequiredCapability = DomainMenuCapabilityTokens.ViewAuthConfiguration,
                                Children = new List<DomainMenuItem>
                                {
                                    new DomainMenuItem { Text = "Sign-in experience", Href = "/config/auth/sign-in" },
                                    new DomainMenuItem { Text = "Entra tenants", Href = "/config/auth/tenants" }
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
                }
            }
        };
    }
}
