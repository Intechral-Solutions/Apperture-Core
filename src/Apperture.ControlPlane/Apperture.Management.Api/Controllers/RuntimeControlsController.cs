using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Runtime;
using Apperture.ControlPlane.Security;
using Apperture.Core.Security.Trust;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/runtime-instances")]
[Authorize]
public sealed class RuntimeControlsController : ControllerBase
{
    private readonly IRuntimeControlService _control;
    private readonly IRuntimeIdentityService _runtimeIdentity;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IPrincipalContext _principalContext;
    private readonly IEnvironmentContext _environment;

    public RuntimeControlsController(
        IRuntimeControlService control,
        IRuntimeIdentityService runtimeIdentity,
        IAuthorizationEvaluationService authorizationEvaluation,
        IPrincipalContext principalContext,
        IEnvironmentContext environment)
    {
        _control = control;
        _runtimeIdentity = runtimeIdentity;
        _authorizationEvaluation = authorizationEvaluation;
        _principalContext = principalContext;
        _environment = environment;
    }

    /// <summary>
    /// Requests a runtime shutdown control intent.
    /// Governance-gated by `runtime.control.shutdown.request` and runtime capability prerequisite checks.
    /// </summary>
    [HttpPost("{runtimeId}/controls/shutdown/request")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> RequestShutdown(
        string runtimeId,
        CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return Unauthorized();

        var environmentId = _environment.EnvironmentId;
        var runtimeScopeKey = ScopeKeyConstants.RuntimeInstance(environmentId, runtimeId);

        var permission = new PermissionDescriptor(
            ActionConstants.RuntimeControlShutdownRequest,
            new ResourceDescriptor(ResourceTypeConstants.RuntimeInstance, runtimeScopeKey));

        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(actor, permission, permission.Resource),
            cancellationToken);
        if (!allowed)
            return Forbid();

        var identity = await _runtimeIdentity.GetByIdAsync(runtimeId, cancellationToken);
        if (identity is null)
            return NotFound();

        if (!identity.DeclaredScopes.Contains(RuntimeCapabilityCatalog.ControlShutdown, StringComparer.Ordinal))
            return Conflict(new { error = "RuntimeMissingShutdownControlCapability" });

        var result = await _control.RequestShutdownAsync(runtimeId, cancellationToken).ConfigureAwait(false);
        if (result is null)
            return Conflict(new { error = "RuntimeShutdownRequestRejected" });

        return Accepted(new
        {
            requestId = result.Id,
            runtimeId = result.RuntimeId,
            controlKind = result.ControlKind,
            status = result.Status
        });
    }
}

