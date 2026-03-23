using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Management.Api.Models;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/config/auth-overview")]
[Authorize]
public sealed class ConfigAuthOverviewController : ControllerBase
{
    private readonly IAuthenticationAdminOverviewService _overview;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;

    public ConfigAuthOverviewController(
        IAuthenticationAdminOverviewService overview,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation)
    {
        _overview = overview;
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ConfigAuthOverviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ConfigAuthOverviewResponse>> Get(CancellationToken cancellationToken)
    {
        if (!await AuthorizeReadAsync(cancellationToken))
            return Forbid();

        var o = await _overview.GetAsync(cancellationToken);
        return Ok(new ConfigAuthOverviewResponse
        {
            LocalAuthEnabled = o.LocalAuthEnabled,
            EntraEnabledInDeploymentConfiguration = o.EntraEnabledInDeploymentConfiguration,
            EntraConfigurationValid = o.EntraConfigurationValid,
            EntraMisconfiguredReason = o.EntraMisconfiguredReason,
            ActiveEntraRegistrationId = o.ActiveEntraRegistrationId,
            ActiveEntraRegistrationDisplayName = o.ActiveEntraRegistrationDisplayName,
            EntraSignInUsesActiveRegistration = o.EntraSignInUsesActiveRegistration,
            ShowLocalSignInInPrimaryChooser = o.ShowLocalSignInInPrimaryChooser,
            PreferEntraInChooser = o.PreferEntraInChooser,
            AutoRedirectToEntra = o.AutoRedirectToEntra,
            LocalShownOnPrimaryLogin = o.LocalShownOnPrimaryLogin,
            LocalFallbackOnly = o.LocalFallbackOnly,
            EntraSignInEnabled = o.EntraSignInEnabled,
            EffectiveExperience = o.EffectiveExperience.ToString(),
            EffectiveExperienceDescription = o.EffectiveExperienceDescription,
            Warnings = o.Warnings
        });
    }

    private async Task<bool> AuthorizeReadAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return false;

        var read = new PermissionDescriptor(
            ActionConstants.AuthConfigurationRead,
            new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance));

        if (await _authorizationEvaluation.EvaluateAsync(
                new AuthorizationEvaluationContext(actor, read, read.Resource),
                cancellationToken))
            return true;

        var policyManage = new PermissionDescriptor(
            ActionConstants.AuthUiPolicyManage,
            new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance));

        return await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(actor, policyManage, policyManage.Resource),
            cancellationToken);
    }
}
