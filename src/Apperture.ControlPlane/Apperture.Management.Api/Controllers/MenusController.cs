using Apperture.Management.Ui.Shared.Models;
using Apperture.Management.Ui.Shared;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// API endpoint for serving Control Panel navigation menus.
/// Navigation contributions are lifecycle-governed via <see cref="IModuleRegistryService.GetEnabledDescriptorsAsync"/> (Epic 27 — Epic27-Execution-Surface-Inventory.md).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MenusController : ControllerBase
{
    private readonly ILogger<MenusController> _logger;
    private readonly IModuleRegistryService _moduleRegistryService;

    public MenusController(ILogger<MenusController> logger, IModuleRegistryService moduleRegistryService)
    {
        _logger = logger;
        _moduleRegistryService = moduleRegistryService;
    }

    /// <summary>
    /// Gets the navigation menu structure. Returns domains with their menu items.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Dictionary<string, DomainMenu>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMenus(CancellationToken cancellationToken)
    {
        _logger.LogDebug("GET /api/menus");
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var enabledDescriptors = await _moduleRegistryService.GetEnabledDescriptorsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var navigation in enabledDescriptors.SelectMany(x => x.Contributions).OfType<NavigationContributionDescriptor>())
        {
            if (!menus.TryGetValue(navigation.Domain, out var domain))
            {
                domain = new DomainMenu { Title = null };
                menus[navigation.Domain] = domain;
            }

            if (domain.Items.Any(x => string.Equals(x.Href, navigation.Route, StringComparison.OrdinalIgnoreCase)))
                continue;
            domain.Items.Add(new DomainMenuItem { Text = navigation.Label, Href = navigation.Route });
        }
        return Ok(menus);
    }
}
