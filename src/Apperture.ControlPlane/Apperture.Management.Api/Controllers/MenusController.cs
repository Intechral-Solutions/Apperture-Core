using Apperture.Management.Ui.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// API endpoint for serving Control Panel navigation menus.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MenusController : ControllerBase
{
    private readonly ILogger<MenusController> _logger;

    public MenusController(ILogger<MenusController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the navigation menu structure. Returns domains with their menu items.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Dictionary<string, DomainMenu>), StatusCodes.Status200OK)]
    public IActionResult GetMenus()
    {
        _logger.LogDebug("GET /api/menus");
        var menus = GetStaticMenuStructure();
        return Ok(menus);
    }

    private static Dictionary<string, DomainMenu> GetStaticMenuStructure()
    {
        return new Dictionary<string, DomainMenu>(StringComparer.OrdinalIgnoreCase)
        {
            ["Runs"] = new DomainMenu
            {
                Title = null,
                Items =
                {
                    new DomainMenuItem { Text = "Runs Console", Href = "/runs" },
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
            }
        };
    }
}
