using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Trust;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Runtime enrollment endpoints. Short-lived tokens; operational auth uses registered identity.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class EnrollController : ControllerBase
{
    private readonly IRuntimeEnrollmentService _enrollment;
    private readonly IPrincipalContext _principalContext;
    private readonly IPrincipalToUserResolver _principalToUserResolver;
    private readonly IRoleProvider _roleProvider;

    public EnrollController(
        IRuntimeEnrollmentService enrollment,
        IPrincipalContext principalContext,
        IPrincipalToUserResolver principalToUserResolver,
        IRoleProvider roleProvider)
    {
        _enrollment = enrollment;
        _principalContext = principalContext;
        _principalToUserResolver = principalToUserResolver;
        _roleProvider = roleProvider;
    }

    /// <summary>
    /// Creates an enrollment invitation for a runtime. Requires an authenticated platform administrator.
    /// </summary>
    [HttpPost("invitation")]
    public async ValueTask<ActionResult<EnrollmentInvitation>> CreateInvitation(
        [FromBody] CreateInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return Unauthorized();

        var userId = await _principalToUserResolver.ResolveUserIdAsync(actor, cancellationToken);
        if (userId is null)
            return Forbid();

        var roles = await _roleProvider.GetRolesForUserAsync(userId!, cancellationToken);
        var isPlatformAdmin = roles.Any(r => r.Id.Value == BootstrapConstants.PlatformAdminRoleId);
        if (!isPlatformAdmin)
            return Forbid();

        var ttl = request.TtlHours.HasValue ? TimeSpan.FromHours(request.TtlHours.Value) : (TimeSpan?)null;
        var invitation = await _enrollment.CreateInvitationAsync(
            request.RuntimeId,
            ttl,
            request.Scopes,
            cancellationToken).ConfigureAwait(false);
        return Ok(invitation);
    }

    /// <summary>
    /// Enrolls a runtime using an enrollment token. Registers runtime identity.
    /// </summary>
    [HttpPost]
    public async ValueTask<ActionResult<EnrollmentResult>> Enroll(
        [FromBody] EnrollRequest request,
        CancellationToken cancellationToken)
    {
        var registration = new RuntimeIdentityRegistration(
            request.RuntimeName,
            request.CredentialType ?? "PublicKey",
            request.PublicKeyOrThumbprint,
            request.ManagedIdentityPrincipalId);

        var result = await _enrollment.EnrollAsync(
            request.Token,
            registration,
            cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

/// <summary>
/// Request to create an enrollment invitation.
/// </summary>
public sealed record CreateInvitationRequest(
    string RuntimeId,
    int? TtlHours = null,
    IReadOnlySet<string>? Scopes = null);

/// <summary>
/// Request to enroll a runtime.
/// </summary>
public sealed record EnrollRequest(
    string Token,
    string RuntimeName,
    string PublicKeyOrThumbprint,
    string? CredentialType = null,
    string? ManagedIdentityPrincipalId = null);
