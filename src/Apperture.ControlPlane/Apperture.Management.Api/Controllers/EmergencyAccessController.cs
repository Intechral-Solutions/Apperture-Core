using Apperture.Core.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Instance emergency-access posture for authorized operators. Secrets are never returned.
/// </summary>
[ApiController]
[Route("api/emergency-access")]
[Authorize]
public sealed class EmergencyAccessController : ControllerBase
{
    private readonly IUserManagementService _userManagement;

    public EmergencyAccessController(IUserManagementService userManagement)
    {
        _userManagement = userManagement;
    }

    /// <summary>Requires user.read. Does not include TOTP secrets, recovery codes, or passwords.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(EmergencyAccessPostureDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<EmergencyAccessPostureDto>> Get(CancellationToken cancellationToken)
    {
        try
        {
            var posture = await _userManagement.GetEmergencyAccessPostureAsync(cancellationToken);
            return Ok(new EmergencyAccessPostureDto(
                posture.IsHealthy,
                posture.FallbackSafeguardRequired,
                posture.DesignatedUserId,
                posture.DesignatedDisplayName,
                posture.DesignatedIsPlatformAdministrator,
                posture.LocalCredentialEnabled,
                posture.NativeMfaEnrolled,
                posture.UnusedRecoveryCodeCount > 0,
                posture.UnusedRecoveryCodeCount,
                posture.DesignatedUsername,
                posture.CanonicalIdentityApplied,
                posture.CanonicalUsernameConflict,
                posture.HasEntraIdentityLink));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}

public sealed record EmergencyAccessPostureDto(
    bool IsHealthy,
    bool FallbackSafeguardRequired,
    string? DesignatedUserId,
    string? DesignatedDisplayName,
    bool DesignatedIsPlatformAdministrator,
    bool LocalCredentialEnabled,
    bool NativeMfaEnrolled,
    bool RecoveryCodesPresent,
    int UnusedRecoveryCodeCount = 0,
    string? DesignatedUsername = null,
    bool CanonicalIdentityApplied = false,
    bool CanonicalUsernameConflict = false,
    bool HasEntraIdentityLink = false);
