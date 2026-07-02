using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Auth administration: user list, detail, create, identity links, roles, and local credential. All endpoints require appropriate auth-admin permissions.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly IUserManagementService _userManagement;
    private readonly ILocalCredentialRepository _localCredentialRepository;
    private readonly IRoleProvider _roleProvider;

    public UsersController(IUserManagementService userManagement, ILocalCredentialRepository localCredentialRepository, IRoleProvider roleProvider)
    {
        _userManagement = userManagement;
        _localCredentialRepository = localCredentialRepository;
        _roleProvider = roleProvider;
    }

    /// <summary>
    /// Lists users with pagination. Requires user.read.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(UserListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserListResponse>> List(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (page, totalCount) = await _userManagement.GetUserPageAsync(skip, take, cancellationToken);
            var items = page.Select(ToListDto).ToList();
            return Ok(new UserListResponse(items, totalCount));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Gets a user by id. Requires user.read.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(UserDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDetailDto>> Get(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _userManagement.GetUserByIdAsync(new UserId(id), cancellationToken);
            if (user is null)
                return NotFound();
            bool? hasLocalCredential = null;
            bool? isCredentialDisabled = null;
            if (user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
            {
                var cred = await _localCredentialRepository.GetByUserIdAsync(user.Id, cancellationToken);
                hasLocalCredential = cred is not null;
                isCredentialDisabled = cred?.IsDisabled;
            }
            return Ok(ToDetailDto(user, hasLocalCredential, isCredentialDisabled));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Gets effective access for a user (merged permissions from assigned roles). Requires user.read.
    /// Reflects role-membership-based effective permissions; runtime authorization also applies scope containment.
    /// </summary>
    [HttpGet("{id}/effective-access")]
    [ProducesResponseType(typeof(PrincipalEffectiveAccessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PrincipalEffectiveAccessDto>> GetEffectiveAccess(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = new UserId(id);
            var user = await _userManagement.GetUserByIdAsync(userId, cancellationToken);
            if (user is null)
                return NotFound();

            var roles = await _roleProvider.GetRolesForUserAsync(userId, cancellationToken);
            var roleIds = roles.Select(r => r.Id.Value).ToList();
            var permissions = roles
                .SelectMany(r => r.Permissions)
                .Distinct()
                .Select(p => new RolePermissionEntryDto(p.Action, p.Resource?.ResourceType, p.Resource?.ScopeKey))
                .OrderBy(p => p.Action, StringComparer.Ordinal)
                .ThenBy(p => p.ResourceType, StringComparer.Ordinal)
                .ThenBy(p => p.ScopeKey, StringComparer.Ordinal)
                .ToList();

            return Ok(new PrincipalEffectiveAccessDto(userId.Value, roleIds, permissions));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Creates a new user with one initial identity link. Optional password when identitySource is Local. Requires user.manage.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateUserResponse>> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.ExternalId))
            return BadRequest(new { message = "DisplayName and ExternalId are required." });
        if (!Enum.TryParse<IdentitySource>(request.IdentitySource, ignoreCase: true, out var source) ||
            (source != IdentitySource.Entra && source != IdentitySource.Local))
            return BadRequest(new { message = "IdentitySource must be 'Entra' or 'Local'." });

        try
        {
            var userId = await _userManagement.CreateUserAsync(
                request.DisplayName.Trim(),
                source,
                request.ExternalId.Trim(),
                request.Password,
                cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = userId.Value }, new CreateUserResponse(userId.Value));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Adds an identity link to a user. Requires user.manage.
    /// </summary>
    [HttpPost("{id}/identity-links")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddIdentityLink(
        string id,
        [FromBody] AddIdentityLinkRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalId))
            return BadRequest(new IdentityLinkErrorResponse(
                "ExternalId is required.",
                IdentityLinkErrorCodes.MissingExternalId));
        if (!Enum.TryParse<IdentitySource>(request.IdentitySource, ignoreCase: true, out var source) ||
            (source != IdentitySource.Entra && source != IdentitySource.Local))
            return BadRequest(new IdentityLinkErrorResponse(
                "IdentitySource must be 'Entra' or 'Local'.",
                IdentityLinkErrorCodes.InvalidSource));

        try
        {
            await _userManagement.AddIdentityLinkAsync(new UserId(id), source, request.ExternalId.Trim(), cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (IdentityLinkException ex) when (ex.Code == IdentityLinkErrorCodes.UserNotFound)
        {
            return NotFound(new IdentityLinkErrorResponse(ex.Message, ex.Code));
        }
        catch (IdentityLinkException ex) when (ex.Code == IdentityLinkErrorCodes.ExternalIdInUse)
        {
            return Conflict(new IdentityLinkErrorResponse(ex.Message, ex.Code));
        }
        catch (IdentityLinkException ex)
        {
            return BadRequest(new IdentityLinkErrorResponse(ex.Message, ex.Code));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            return Conflict(new IdentityLinkErrorResponse(
                "An identity link with this source and external id is already attached to another user.",
                IdentityLinkErrorCodes.ExternalIdInUse));
        }
    }

    /// <summary>
    /// Removes an identity link from a user. Cannot remove the last link. Requires user.manage.
    /// </summary>
    [HttpDelete("{id}/identity-links")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveIdentityLink(
        string id,
        [FromQuery] string source,
        [FromQuery] string externalId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            return BadRequest(new { message = "externalId query parameter is required." });
        if (!Enum.TryParse<IdentitySource>(source, ignoreCase: true, out var sourceEnum) ||
            (sourceEnum != IdentitySource.Entra && sourceEnum != IdentitySource.Local))
            return BadRequest(new { message = "source must be 'Entra' or 'Local'." });

        try
        {
            await _userManagement.RemoveIdentityLinkAsync(new UserId(id), sourceEnum, externalId, cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("last identity link"))
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Assigns a role to a user. Requires role.assign.
    /// </summary>
    [HttpPost("{id}/roles")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignRole(
        string id,
        [FromBody] AssignRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RoleId))
            return BadRequest(new { message = "RoleId is required." });

        try
        {
            await _userManagement.AssignRoleAsync(new UserId(id), new RoleId(request.RoleId), cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Removes a role from a user. Requires role.assign.
    /// </summary>
    [HttpDelete("{id}/roles/{roleId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveRole(string id, string roleId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _userManagement.RemoveRoleAsync(new UserId(id), new RoleId(roleId), cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Sets or resets the local password for a user. User must have a Local identity link. Requires credential.manage.
    /// </summary>
    [HttpPost("{id}/local-credential/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetLocalPassword(
        string id,
        [FromBody] SetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.NewPassword is null)
            return BadRequest(new { message = "NewPassword is required." });

        try
        {
            await _userManagement.SetLocalPasswordAsync(new UserId(id), request.NewPassword, cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("not found") || ex.Message.Contains("no Local identity"))
                return NotFound(new { message = ex.Message });
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Enables or disables the local credential for a user. Requires credential.manage.
    /// </summary>
    [HttpPatch("{id}/local-credential")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetLocalCredentialDisabled(
        string id,
        [FromBody] SetLocalCredentialDisabledRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _userManagement.SetLocalCredentialDisabledAsync(new UserId(id), request.IsDisabled, cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("not found") || ex.Message.Contains("no Local") || ex.Message.Contains("no local credential"))
                return NotFound(new { message = ex.Message });
            return BadRequest(new { message = ex.Message });
        }
    }

    private static UserListDto ToListDto(User user)
    {
        var hasLocal = user.IdentityLinks.Any(l => l.Source == IdentitySource.Local);
        var hasEntra = user.IdentityLinks.Any(l => l.Source == IdentitySource.Entra);
        return new UserListDto(
            user.Id.Value,
            user.DisplayName,
            hasLocal,
            hasEntra,
            user.AssignedRoleIds.Select(r => r.Value).ToArray());
    }

    private static UserDetailDto ToDetailDto(User user, bool? hasLocalCredential, bool? isCredentialDisabled)
    {
        return new UserDetailDto(
            user.Id.Value,
            user.DisplayName,
            user.IdentityLinks.Select(l => new IdentityLinkDto(l.Source.ToString(), l.ExternalId, l.LastAuthenticatedAt)).ToList(),
            user.AssignedRoleIds.Select(r => r.Value).ToList(),
            hasLocalCredential,
            isCredentialDisabled);
    }
}

public sealed record UserListResponse(IReadOnlyList<UserListDto> Items, int TotalCount);

public sealed record UserListDto(
    string UserId,
    string DisplayName,
    bool HasLocalLink,
    bool HasEntraLink,
    string[] RoleIds);

public sealed record UserDetailDto(
    string UserId,
    string DisplayName,
    IReadOnlyList<IdentityLinkDto> IdentityLinks,
    IReadOnlyList<string> RoleIds,
    bool? HasLocalCredential,
    bool? IsCredentialDisabled);

public sealed record IdentityLinkDto(string Source, string ExternalId, DateTimeOffset? LastAuthenticatedAt);

public sealed record CreateUserRequest(string DisplayName, string IdentitySource, string ExternalId, string? Password = null);

public sealed record CreateUserResponse(string UserId);

public sealed record AddIdentityLinkRequest(string IdentitySource, string ExternalId);

public sealed record IdentityLinkErrorResponse(string Message, string Code);

public sealed record AssignRoleRequest(string RoleId);

public sealed record SetPasswordRequest(string? NewPassword);

public sealed record SetLocalCredentialDisabledRequest(bool IsDisabled);
