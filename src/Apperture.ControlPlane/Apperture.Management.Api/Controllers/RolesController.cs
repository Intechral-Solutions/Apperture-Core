using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Models;
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
            return Ok(roles.Select(r => new RoleDto(r.Id.Value, r.Name, r.Description, r.IsSystemRole)).ToList());
        }
        catch (Exception ex) when (IsRoleFailure(ex))
        {
            return MapRoleFailure(ex);
        }
    }

    /// <summary>
    /// Gets a role by id with its granted permissions and current assignment count. Requires user.read.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(RoleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoleDetailDto>> Get(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var role = await _userManagement.GetRoleAsync(new RoleId(id), cancellationToken);
            if (role is null)
                return NotFound();
            var assignments = await _userManagement.GetRoleAssignmentsAsync(role.Id, cancellationToken);
            if (assignments is null)
                return NotFound();
            return Ok(ToDetail(role, assignments.Count));
        }
        catch (Exception ex) when (IsRoleFailure(ex))
        {
            return MapRoleFailure(ex);
        }
    }

    /// <summary>
    /// Creates a user-created role. Requires role.manage. Non-empty initial grants also require permission.assign.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RoleDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoleDetailDto>> Create([FromBody] CreateRoleRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var role = await _userManagement.CreateRoleAsync(
                request.Name,
                request.Description,
                ToPermissionDescriptors(request.Permissions),
                cancellationToken);
            // The role is committed. Reporting it needs only role.manage, which the caller has: the holders list would need user.read.
            var detail = ToDetail(role, await _userManagement.CountRoleAssignmentsAsync(role.Id, cancellationToken));
            return CreatedAtAction(nameof(Get), new { id = role.Id.Value }, detail);
        }
        catch (Exception ex) when (IsRoleFailure(ex))
        {
            return MapRoleFailure(ex);
        }
    }

    /// <summary>
    /// Updates role name and description. Requires role.manage. Does not replace grants.
    /// </summary>
    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(RoleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoleDetailDto>> UpdateMetadata(string id, [FromBody] UpdateRoleMetadataRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var roleId = new RoleId(id);
            var role = await _userManagement.UpdateRoleMetadataAsync(roleId, request.Name, request.Description, cancellationToken);
            return Ok(ToDetail(role, await _userManagement.CountRoleAssignmentsAsync(role.Id, cancellationToken)));
        }
        catch (Exception ex) when (IsRoleFailure(ex))
        {
            return MapRoleFailure(ex);
        }
    }

    /// <summary>
    /// Hard-deletes a user-created role with zero assignments. Requires role.manage.
    /// Does not remove assignments.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await _userManagement.DeleteRoleAsync(new RoleId(id), cancellationToken);
            return NoContent();
        }
        catch (Exception ex) when (IsRoleFailure(ex))
        {
            return MapRoleFailure(ex);
        }
    }

    /// <summary>
    /// Lists current holders for delete confirmation. Requires the same user.read check as role detail.
    /// Returns only user id, username and display name.
    /// </summary>
    [HttpGet("{id}/assignments")]
    [ProducesResponseType(typeof(RoleAssignmentsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoleAssignmentsDto>> GetAssignments(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var assignments = await _userManagement.GetRoleAssignmentsAsync(new RoleId(id), cancellationToken);
            if (assignments is null)
                return NotFound();
            var holders = assignments.Holders
                .Select(h => new RoleAssignmentHolderDto(h.UserId.Value, h.Username, h.DisplayName))
                .ToList();
            return Ok(new RoleAssignmentsDto(assignments.Count, holders));
        }
        catch (Exception ex) when (IsRoleFailure(ex))
        {
            return MapRoleFailure(ex);
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
            var permissions = await _userManagement.GetRolePermissionsAsync(new RoleId(id), cancellationToken);
            return Ok(permissions.Select(ToRolePermission).ToList());
        }
        catch (Exception ex) when (IsRoleFailure(ex))
        {
            return MapRoleFailure(ex);
        }
    }

    [HttpPut("{id}/permissions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetPermissions(string id, [FromBody] SetRolePermissionsRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Permissions is null)
            return BadRequest(new { message = "Permissions are required." });

        try
        {
            await _userManagement.SetRolePermissionsAsync(
                new RoleId(id),
                request.Permissions.Select(ToPermissionDescriptor).ToList(),
                cancellationToken);
            return NoContent();
        }
        catch (Exception ex) when (IsRoleFailure(ex))
        {
            return MapRoleFailure(ex);
        }
    }

    private static RoleDetailDto ToDetail(Role role, int assignmentCount)
    {
        var permissions = role.Permissions
            .Select(p => new RolePermissionEntryDto(p.Action, p.Resource?.ResourceType, p.Resource?.ScopeKey))
            .ToList();
        return new RoleDetailDto(role.Id.Value, role.Name, role.Description, role.IsSystemRole, permissions, assignmentCount);
    }

    private static IReadOnlyList<PermissionDescriptor>? ToPermissionDescriptors(IReadOnlyList<RolePermissionDto>? permissions) =>
        permissions?.Select(ToPermissionDescriptor).ToList();

    // Only the typed outcomes are business results. An InvalidOperationException (an audit programming error included) is not
    // one of them and reaches the host's exception handler as the internal error it is.
    private static bool IsRoleFailure(Exception ex) =>
        ex is UnauthorizedAccessException || GovernanceFailureResults.IsGovernanceOutcome(ex);

    private static ActionResult MapRoleFailure(Exception ex) => ex is UnauthorizedAccessException
        ? new ForbidResult()
        : GovernanceFailureResults.Map(ex, legacyErrorProperty: true);

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

public sealed record RoleDto(string Id, string Name, string? Description, bool IsSystemRole = false);
public sealed record RolePermissionDto(string Action, string? ResourceType, string? ScopeKey);
public sealed record SetRolePermissionsRequest(IReadOnlyList<RolePermissionDto> Permissions);

/// <summary>Create body. Id and system-role flag are not accepted.</summary>
public sealed record CreateRoleRequest(
    string Name,
    string? Description = null,
    IReadOnlyList<RolePermissionDto>? Permissions = null);

/// <summary>Metadata body. Grants are not accepted.</summary>
public sealed record UpdateRoleMetadataRequest(string Name, string? Description);
