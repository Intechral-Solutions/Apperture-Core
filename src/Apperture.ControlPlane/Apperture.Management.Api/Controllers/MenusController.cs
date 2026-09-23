using Apperture.Management.Ui.Shared.Models;
using Apperture.Management.Ui.Shared;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.Models;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// API endpoint for serving Control Panel navigation menus.
/// Module contributions may attach only to known rail domains.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MenusController : ControllerBase
{
    private readonly ILogger<MenusController> _logger;
    private readonly IModuleRegistryService _moduleRegistryService;
    private readonly IOperatorCapabilitiesQueryService _capabilities;

    public MenusController(
        ILogger<MenusController> logger,
        IModuleRegistryService moduleRegistryService,
        IOperatorCapabilitiesQueryService capabilities)
    {
        _logger = logger;
        _moduleRegistryService = moduleRegistryService;
        _capabilities = capabilities;
    }

    [HttpGet]
    [ProducesResponseType(typeof(Dictionary<string, DomainMenu>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMenus(CancellationToken cancellationToken)
    {
        _logger.LogDebug("GET /api/menus");
        var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
        var enabledDescriptors = await _moduleRegistryService.GetEnabledDescriptorsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var navigation in enabledDescriptors.SelectMany(x => x.Contributions).OfType<NavigationContributionDescriptor>())
        {
            if (!SectionOwnerConstants.KnownRailDomains.Contains(navigation.Domain))
            {
                _logger.LogDebug(
                    "Ignoring module navigation contribution for unknown rail domain {Domain} ({Route}).",
                    navigation.Domain,
                    navigation.Route);
                continue;
            }

            if (!menus.TryGetValue(navigation.Domain, out var domain))
                continue;

            if (domain.Items.Any(x => string.Equals(x.Href, navigation.Route, StringComparison.OrdinalIgnoreCase)))
                continue;
            domain.Items.Add(new DomainMenuItem { Text = navigation.Label, Href = navigation.Route });
        }

        var caps = await _capabilities.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        menus = DomainMenuCapabilityFilter.Apply(menus, token => HasCapability(caps, token));
        return Ok(menus);
    }

    internal static bool HasCapability(OperatorCapabilitiesDto caps, string token) =>
        token switch
        {
            DomainMenuCapabilityTokens.ViewAudit => caps.CanViewAudit,
            DomainMenuCapabilityTokens.ViewIntegrations => caps.CanViewIntegrations,
            DomainMenuCapabilityTokens.ViewConnections => caps.CanViewConnections,
            DomainMenuCapabilityTokens.ViewGovernance => caps.CanViewGovernance,
            DomainMenuCapabilityTokens.ViewManagedContacts => caps.CanViewManagedContacts,
            DomainMenuCapabilityTokens.ViewNotificationDefinitions => caps.CanViewNotificationDefinitions,
            DomainMenuCapabilityTokens.ViewPlatformSettings => caps.CanViewPlatformSettings,
            DomainMenuCapabilityTokens.ManageModules => caps.CanManageModules,
            DomainMenuCapabilityTokens.ViewAuthConfiguration => caps.CanViewAuthConfiguration,
            DomainMenuCapabilityTokens.ManageSchedules => caps.CanManageSchedules,
            DomainMenuCapabilityTokens.ViewComponents => caps.CanViewConnectorDefinitions || caps.CanViewNotificationDefinitions,
            _ => true
        };
}
