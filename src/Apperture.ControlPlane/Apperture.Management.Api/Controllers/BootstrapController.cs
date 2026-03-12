using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// First-admin bootstrap endpoint. Succeeds only when no platform users exist. No auth required; call once after deploy before enabling real auth.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public sealed class BootstrapController : ControllerBase
{
    private readonly IFirstAdminBootstrapService _bootstrap;

    public BootstrapController(IFirstAdminBootstrapService bootstrap)
    {
        _bootstrap = bootstrap;
    }

    /// <summary>
    /// Creates the first platform administrator. Idempotent in the sense that once any user exists, the endpoint returns 409.
    /// </summary>
    [HttpPost("first-admin")]
    public async Task<ActionResult<FirstAdminResponse>> CreateFirstAdmin(
        [FromBody] FirstAdminRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.ExternalId))
            return BadRequest(new { message = "DisplayName and ExternalId are required." });

        if (!Enum.TryParse<IdentitySource>(request.IdentitySource, ignoreCase: true, out var source) ||
            (source != IdentitySource.Entra && source != IdentitySource.Local))
            return BadRequest(new { message = "IdentitySource must be 'Entra' or 'Local'." });

        var result = await _bootstrap.TryCreateFirstAdminAsync(
            request.DisplayName.Trim(),
            source,
            request.ExternalId.Trim(),
            request.Password,
            cancellationToken);

        return result.Kind switch
        {
            FirstAdminBootstrapResultKind.Success => Created(
                $"/api/bootstrap/first-admin",
                new FirstAdminResponse(result.UserId!.Value)),
            FirstAdminBootstrapResultKind.AlreadyExists => Conflict(new { message = "First admin already exists; at least one platform user is present." }),
            FirstAdminBootstrapResultKind.RoleNotFound => StatusCode(503, new { message = "Platform administrator role not found; ensure migrations have been applied." }),
            FirstAdminBootstrapResultKind.DuplicateIdentityLink => Conflict(new { message = "An identity link with this source and external id already exists." }),
            _ => StatusCode(500, new { message = "Unexpected bootstrap result." })
        };
    }
}

/// <summary>
/// Request to create the first platform administrator. When IdentitySource is Local, optional Password creates the local credential.
/// </summary>
public sealed record FirstAdminRequest(string DisplayName, string IdentitySource, string ExternalId, string? Password = null);

/// <summary>
/// Response when first admin is created (userId only; no secrets).
/// </summary>
public sealed record FirstAdminResponse(string UserId);
