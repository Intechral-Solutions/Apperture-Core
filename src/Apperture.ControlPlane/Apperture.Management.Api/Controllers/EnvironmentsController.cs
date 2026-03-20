using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Environment governance inspection surface (single-tenant: current environment).
/// </summary>
[ApiController]
[Route("api/environments")]
[Authorize]
public sealed class EnvironmentsController : ControllerBase
{
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IPrincipalContext _principalContext;
    private readonly IEnvironmentContext _environment;

    public EnvironmentsController(
        IAuthorizationEvaluationService authorizationEvaluation,
        IPrincipalContext principalContext,
        IEnvironmentContext environment)
    {
        _authorizationEvaluation = authorizationEvaluation;
        _principalContext = principalContext;
        _environment = environment;
    }

    /// <summary>
    /// Gets the current environment summary. Requires environment.read on the environment scope.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<EnvironmentSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async ValueTask<ActionResult<IReadOnlyList<EnvironmentSummaryDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return Unauthorized();

        var environmentId = _environment.EnvironmentId;
        var envScopeKey = ScopeKeyConstants.Environment(environmentId);

        var permission = new PermissionDescriptor(
            ActionConstants.EnvironmentRead,
            new ResourceDescriptor(ResourceTypeConstants.Environment, envScopeKey));

        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(actor, permission, permission.Resource),
            cancellationToken);
        if (!allowed)
            return Forbid();

        return Ok(new List<EnvironmentSummaryDto> { new(environmentId, environmentId) });
    }
}

public sealed record EnvironmentSummaryDto(string EnvironmentId, string DisplayName);

