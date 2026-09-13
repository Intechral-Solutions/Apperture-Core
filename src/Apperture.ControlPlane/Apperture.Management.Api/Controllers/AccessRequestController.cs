using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/access-requests")]
[Authorize(AuthenticationSchemes = EntraAccessRequestAuthentication.Scheme)]
public sealed class AccessRequestController : ControllerBase
{
    private readonly IEntraAccessRequestService _requests;

    public AccessRequestController(IEntraAccessRequestService requests) => _requests = requests;

    [HttpGet("session")]
    public async Task<ActionResult<EntraAccessRequestSessionDto>> Session(CancellationToken cancellationToken)
    {
        var principal = EntraAccessRequestAuthentication.TryRead(User);
        if (principal is null)
            return Unauthorized();

        var snapshot = await _requests.GetSessionSnapshotAsync(principal, cancellationToken);
        return Ok(ToDto(snapshot));
    }

    [HttpPost]
    public async Task<ActionResult<EntraAccessRequestSessionDto>> Submit(CancellationToken cancellationToken)
    {
        var principal = EntraAccessRequestAuthentication.TryRead(User);
        if (principal is null)
            return Unauthorized();

        try
        {
            await _requests.SubmitAsync(principal, cancellationToken);
            var snapshot = await _requests.GetSessionSnapshotAsync(principal, cancellationToken);
            return Ok(ToDto(snapshot));
        }
        catch (EntraAccessRequestException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }
    }

    [HttpPost("sign-out")]
    [AllowAnonymous]
    public async Task<IActionResult> SignOutSession()
    {
        await HttpContext.SignOutAsync(EntraAccessRequestAuthentication.Scheme);
        return NoContent();
    }

    private static EntraAccessRequestSessionDto ToDto(EntraAccessRequestSessionSnapshot snapshot) =>
        new(
            snapshot.State,
            snapshot.DisplayName,
            snapshot.DirectoryAddress,
            snapshot.UserType,
            snapshot.RegistrationDisplayName,
            snapshot.CanRequest,
            snapshot.Message,
            snapshot.PendingRequestId,
            snapshot.RequestedAtUtc);
}

public sealed record EntraAccessRequestSessionDto(
    string State,
    string? DisplayName,
    string? DirectoryAddress,
    string? UserType,
    string? RegistrationDisplayName,
    bool CanRequest,
    string? Message,
    string? PendingRequestId,
    DateTimeOffset? RequestedAtUtc);
