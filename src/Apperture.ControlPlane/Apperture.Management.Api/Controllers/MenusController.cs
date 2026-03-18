using Apperture.Management.Ui.Shared.Models;
using Apperture.Management.Ui.Shared;
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
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        return Ok(menus);
    }
}
