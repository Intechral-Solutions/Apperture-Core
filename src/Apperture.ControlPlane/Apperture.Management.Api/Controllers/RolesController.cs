using Apperture.Core.Application.Services;
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
}

public sealed record RoleDto(string Id, string Name, string? Description);
