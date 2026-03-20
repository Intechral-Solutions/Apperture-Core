using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Security.Trust;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Operator-facing runtime inspection surfaces.
/// Invitation scopes are treated as declared metadata (strings), not as platform-governed capabilities.
/// </summary>
[ApiController]
[Route("api/runtime-instances")]
[Authorize]
public sealed class RuntimeInstancesController : ControllerBase
{
    private readonly IRuntimeIdentityService _runtimeIdentity;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IPrincipalContext _principalContext;
    private readonly IEnvironmentContext _environment;

    public RuntimeInstancesController(
        IRuntimeIdentityService runtimeIdentity,
        IAuthorizationEvaluationService authorizationEvaluation,
        IPrincipalContext principalContext,
        IEnvironmentContext environment)
    {
        _runtimeIdentity = runtimeIdentity;
        _authorizationEvaluation = authorizationEvaluation;
        _principalContext = principalContext;
        _environment = environment;
    }

    /// <summary>
    /// Lists runtime declared capability metadata (invitation scopes).
    /// Requires runtime.read on the environment-scoped runtime instance scope.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RuntimeDeclaredMetadataDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async ValueTask<ActionResult<IReadOnlyList<RuntimeDeclaredMetadataDto>>> GetAll(
        CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return Unauthorized();

        var environmentId = _environment.EnvironmentId;
        var envRuntimeScopeKey = ScopeKeyConstants.Environment(environmentId);

        var permission = new PermissionDescriptor(
            ActionConstants.RuntimeRead,
            new ResourceDescriptor(ResourceTypeConstants.RuntimeInstance, envRuntimeScopeKey));

        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(actor, permission, permission.Resource),
            cancellationToken);
        if (!allowed)
            return Forbid();

        // Durable source: operator discovery is backed by the persisted runtime identities table,
        // not any in-memory descriptor registry.
        var identities = await _runtimeIdentity.GetAllAsync(cancellationToken);
        var dtos = identities.Select(i => new RuntimeDeclaredMetadataDto(i.Id, i.DeclaredScopes)).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// Gets runtime declared capability metadata (invitation scopes).
    /// Requires runtime.read on the runtime instance scope.
    /// </summary>
    [HttpGet("{runtimeId}/capabilities")]
    [ProducesResponseType(typeof(RuntimeDeclaredMetadataDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async ValueTask<ActionResult<RuntimeDeclaredMetadataDto>> GetCapabilities(
        string runtimeId,
        CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return Unauthorized();

        var environmentId = _environment.EnvironmentId;
        var runtimeScopeKey = ScopeKeyConstants.RuntimeInstance(environmentId, runtimeId);

        var permission = new PermissionDescriptor(
            ActionConstants.RuntimeRead,
            new ResourceDescriptor(ResourceTypeConstants.RuntimeInstance, runtimeScopeKey));

        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(actor, permission, permission.Resource),
            cancellationToken);
        if (!allowed)
            return Forbid();

        var identity = await _runtimeIdentity.GetByIdAsync(runtimeId, cancellationToken);
        if (identity is null)
            return NotFound();

        return Ok(new RuntimeDeclaredMetadataDto(runtimeId, identity.DeclaredScopes));
    }
}

public sealed record RuntimeDeclaredMetadataDto(string RuntimeId, IReadOnlyList<string> DeclaredScopes);

