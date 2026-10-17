using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ControlPlane.Security;
using Apperture.Management.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/governance")]
[Authorize]
public sealed class GovernanceController : ControllerBase
{
    private const string SourcePlatform = "platform";
    private const string SourceModule = "module";

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

        var resources = _registry.GetResourceDescriptorsWithFamily()
            .Select(r => new ResourceCatalogEntryDto(r.Descriptor.ResourceType, r.Descriptor.ScopeKey, r.Family?.ToString()))
            .OrderBy(r => r.ResourceType, StringComparer.Ordinal)
            .ThenBy(r => r.ScopeKey, StringComparer.Ordinal)
            .ToList();
        var permissions = _registry.GetPermissionDefinitions()
            .Select(ToPermissionCatalogEntry)
            .OrderBy(p => p.Action, StringComparer.Ordinal)
            .ThenBy(p => p.ResourceType, StringComparer.Ordinal)
            .ThenBy(p => p.ScopeKey, StringComparer.Ordinal)
            .ToList();

        return Ok(new GovernanceDescriptorResponse(resources, permissions));
    }

    private static PermissionCatalogEntryDto ToPermissionCatalogEntry(PermissionDefinition definition)
    {
        var descriptor = definition.Identity;
        return new PermissionCatalogEntryDto(
            descriptor.Action,
            descriptor.Resource?.ResourceType,
            descriptor.Resource?.ScopeKey,
            definition.Family?.ToString(),
            definition.Origin == PermissionOrigin.Module ? SourceModule : SourcePlatform,
            definition.Origin == PermissionOrigin.Module ? definition.ModuleId : null,
            definition.DisplayName,
            definition.Description,
            definition.Category.ToString(),
            definition.Sensitivity.ToString(),
            definition.RequiresStepUp);
    }

    [HttpGet("effective-permissions")]
    [ProducesResponseType(typeof(IReadOnlyList<RolePermissionEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<RolePermissionEntryDto>>> GetEffectivePermissions(CancellationToken cancellationToken = default)
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
            return Ok(Array.Empty<RolePermissionEntryDto>());

        var permissions = (await _roleProvider.GetRolesForUserAsync(userId, cancellationToken))
            .SelectMany(r => r.Permissions)
            .Distinct()
            .Select(p => new RolePermissionEntryDto(p.Action, p.Resource?.ResourceType, p.Resource?.ScopeKey))
            .OrderBy(p => p.Action, StringComparer.Ordinal)
            .ThenBy(p => p.ResourceType, StringComparer.Ordinal)
            .ThenBy(p => p.ScopeKey, StringComparer.Ordinal)
            .ToList();
        return Ok(permissions);
    }
}

public sealed record GovernanceDescriptorResponse(
    IReadOnlyList<ResourceCatalogEntryDto> Resources,
    IReadOnlyList<PermissionCatalogEntryDto> Permissions);

