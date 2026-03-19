using Apperture.Core.Application.Services;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Lists platform roles for auth administration (e.g. role assignment). Requires user.read.
/// </summary>
[ApiController]
[Route("api/roles")]
[Authorize]
public sealed class RolesController : ControllerBase
{
    private readonly IUserManagementService _userManagement;

    public RolesController(IUserManagementService userManagement)
    {
        _userManagement = userManagement;
    }

    /// <summary>
    /// Gets all roles. Requires user.read.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken cancellationToken = default)
    {
        try
        {
            var roles = await _userManagement.GetRolesAsync(cancellationToken);
            return Ok(roles.Select(r => new RoleDto(r.Id.Value, r.Name, r.Description)).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{id}/permissions")]
    [ProducesResponseType(typeof(IReadOnlyList<RolePermissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<RolePermissionDto>>> GetPermissions(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var permissions = await _userManagement.GetRolePermissionsAsync(new Apperture.Core.Domain.Identity.RoleId(id), cancellationToken);
            return Ok(permissions.Select(ToRolePermission).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPut("{id}/permissions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetPermissions(string id, [FromBody] SetRolePermissionsRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Permissions is null)
            return BadRequest(new { message = "Permissions are required." });

        try
        {
            await _userManagement.SetRolePermissionsAsync(
                new Apperture.Core.Domain.Identity.RoleId(id),
                request.Permissions.Select(ToPermissionDescriptor).ToList(),
                cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private static RolePermissionDto ToRolePermission(PermissionDescriptor permission) =>
        new(
            permission.Action,
            permission.Resource?.ResourceType,
            permission.Resource?.ScopeKey);

    private static PermissionDescriptor ToPermissionDescriptor(RolePermissionDto permission) =>
        new(
            permission.Action,
            string.IsNullOrWhiteSpace(permission.ResourceType)
                ? null
                : new ResourceDescriptor(
                    permission.ResourceType!,
                    permission.ScopeKey ?? Apperture.Core.Domain.Governance.ScopeKeyConstants.Instance));
}

public sealed record RoleDto(string Id, string Name, string? Description);
public sealed record RolePermissionDto(string Action, string? ResourceType, string? ScopeKey);
public sealed record SetRolePermissionsRequest(IReadOnlyList<RolePermissionDto> Permissions);
