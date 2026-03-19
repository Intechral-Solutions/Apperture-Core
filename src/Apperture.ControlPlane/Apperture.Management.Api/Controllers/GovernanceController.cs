using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ControlPlane.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/governance")]
[Authorize]
public sealed class GovernanceController : ControllerBase
{
    private readonly IResourceDescriptorRegistry _registry;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IPrincipalToUserResolver _principalToUserResolver;
    private readonly IRoleProvider _roleProvider;

    public GovernanceController(
        IResourceDescriptorRegistry registry,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IPrincipalToUserResolver principalToUserResolver,
        IRoleProvider roleProvider)
    {
        _registry = registry;
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _principalToUserResolver = principalToUserResolver;
        _roleProvider = roleProvider;
    }

    [HttpGet("descriptors")]
    [ProducesResponseType(typeof(GovernanceDescriptorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<GovernanceDescriptorResponse>> GetDescriptors(CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return Unauthorized();

        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new Apperture.Shared.Kernel.PermissionDescriptor(ActionConstants.UserRead, new Apperture.Shared.Kernel.ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance)),
                null),
            cancellationToken);
        if (!allowed)
            return Forbid();

        var resources = _registry.GetResourceDescriptors()
            .Select(r => new GovernedResourceDto(r.ResourceType, r.ScopeKey))
            .OrderBy(r => r.ResourceType, StringComparer.Ordinal)
            .ThenBy(r => r.ScopeKey, StringComparer.Ordinal)
            .ToList();
        var permissions = _registry.GetPermissionDescriptors()
            .Select(p => new GovernedPermissionDto(p.Action, p.Resource?.ResourceType, p.Resource?.ScopeKey))
            .OrderBy(p => p.Action, StringComparer.Ordinal)
            .ThenBy(p => p.ResourceType, StringComparer.Ordinal)
            .ThenBy(p => p.ScopeKey, StringComparer.Ordinal)
            .ToList();

        return Ok(new GovernanceDescriptorResponse(resources, permissions));
    }

    [HttpGet("effective-permissions")]
    [ProducesResponseType(typeof(IReadOnlyList<GovernedPermissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<GovernedPermissionDto>>> GetEffectivePermissions(CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return Unauthorized();

        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new Apperture.Shared.Kernel.PermissionDescriptor(ActionConstants.UserRead, new Apperture.Shared.Kernel.ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance)),
                null),
            cancellationToken);
        if (!allowed)
            return Forbid();

        var userId = await _principalToUserResolver.ResolveUserIdAsync(actor, cancellationToken);
        if (userId is null)
            return Ok(Array.Empty<GovernedPermissionDto>());

        var permissions = (await _roleProvider.GetRolesForUserAsync(userId, cancellationToken))
            .SelectMany(r => r.Permissions)
            .Distinct()
            .Select(p => new GovernedPermissionDto(p.Action, p.Resource?.ResourceType, p.Resource?.ScopeKey))
            .OrderBy(p => p.Action, StringComparer.Ordinal)
            .ThenBy(p => p.ResourceType, StringComparer.Ordinal)
            .ThenBy(p => p.ScopeKey, StringComparer.Ordinal)
            .ToList();
        return Ok(permissions);
    }
}

public sealed record GovernanceDescriptorResponse(
    IReadOnlyList<GovernedResourceDto> Resources,
    IReadOnlyList<GovernedPermissionDto> Permissions);

public sealed record GovernedResourceDto(string ResourceType, string ScopeKey);
public sealed record GovernedPermissionDto(string Action, string? ResourceType, string? ScopeKey);

