using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Audit;
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
    private readonly IEntraPrimaryAuthenticationPolicy? _entraPrimaryPolicy;
    private readonly INativeMfaService? _nativeMfa;
    private readonly IAccountInvitationService? _invitations;

    public UsersController(
        IUserManagementService userManagement,
        ILocalCredentialRepository localCredentialRepository,
        IRoleProvider roleProvider,
        IEntraPrimaryAuthenticationPolicy? entraPrimaryPolicy = null,
        INativeMfaService? nativeMfa = null,
        IAccountInvitationService? invitations = null)
    {
        _userManagement = userManagement;
        _localCredentialRepository = localCredentialRepository;
        _roleProvider = roleProvider;
        _entraPrimaryPolicy = entraPrimaryPolicy;
        _nativeMfa = nativeMfa;
        _invitations = invitations;
    }

    /// <summary>
    /// Lists users with pagination, ordered by username. <c>q</c> narrows the list to a username prefix or a
    /// case-insensitive display-name substring (exact username first); email, UPN and user id are never searched.
    /// <c>take</c> defaults to 20 and is clamped to 1-100. Requires user.read.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(UserListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserListResponse>> List(
        [FromQuery] int skip = 0,
        [FromQuery] int take = UserSearchCriteria.DefaultTake,
        [FromQuery] string? q = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (page, totalCount) = await _userManagement.GetUserPageAsync(skip, take, cancellationToken, q);
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
            LocalCredentialData? cred = null;
            if (user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
            {
                cred = await _localCredentialRepository.GetByUserIdAsync(user.Id, cancellationToken);
                hasLocalCredential = cred is not null;
                isCredentialDisabled = cred?.IsDisabled;
            }

            var presentation = _entraPrimaryPolicy is null
                ? null
                : await _entraPrimaryPolicy.DescribeAsync(user, cred, cancellationToken);
            var nativeMfaEnrolled = _nativeMfa is not null && await _nativeMfa.IsEnrolledAsync(user.Id, cancellationToken);
            var unusedCodes = nativeMfaEnrolled && _nativeMfa is not null
                ? await _nativeMfa.CountUnusedRecoveryCodesAsync(user.Id, cancellationToken)
                : 0;
            var invitation = _invitations is null
                ? null
                : await _invitations.GetStatusAsync(user.Id, cancellationToken);
            return Ok(ToDetailDto(user, hasLocalCredential, isCredentialDisabled, presentation, nativeMfaEnrolled, unusedCodes > 0, invitation));
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
    /// Creates a new user with one initial identity link. Local identity requires a password. Requires user.manage.
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
        if (!Enum.TryParse<IdentitySource>(request.IdentitySource, ignoreCase: true, out var source) ||
            (source != IdentitySource.Entra && source != IdentitySource.Local))
            return BadRequest(new { message = "IdentitySource must be 'Entra' or 'Local'." });

        // Local: Username is the preferred field and ExternalId an accepted alias for the same name. Supplying both is
        // allowed only when they are the same name, so a request can never create a different account name than one
        // of its fields asked for. Entra keeps ExternalId as its only key; Username does not apply.
        var requestedName = request.ExternalId;
        if (source == IdentitySource.Local)
        {
            var hasUsername = !string.IsNullOrWhiteSpace(request.Username);
            var hasExternalId = !string.IsNullOrWhiteSpace(request.ExternalId);
            if (hasUsername && hasExternalId
                && !string.Equals(UsernamePolicy.Normalize(request.Username!), UsernamePolicy.Normalize(request.ExternalId!), StringComparison.Ordinal))
            {
                return BadRequest(new
                {
                    message = "Username and ExternalId are the same field for a Local user; supply one, or the same name in both.",
                    code = UsernameErrorCodes.Mismatch
                });
            }

            requestedName = hasUsername ? request.Username : request.ExternalId;
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(requestedName))
            return BadRequest(new { message = source == IdentitySource.Local
                ? "DisplayName and Username are required."
                : "DisplayName and ExternalId are required." });

        try
        {
            var userId = await _userManagement.CreateUserAsync(
                request.DisplayName.Trim(),
                source,
                requestedName.Trim(),
                request.Password,
                cancellationToken,
                request.ContactEmail,
                request.SendSetupInvitation);
            return CreatedAtAction(nameof(Get), new { id = userId.Value }, new CreateUserResponse(userId.Value));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (AccountSecurityException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code, deliveryFailed = ex.Code == AccountSecurityErrorCodes.SmtpNotConfigured });
        }
        catch (EmergencyAccessException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }
        catch (IdentityLinkException ex)
        {
            return BadRequest(new { message = ex.Message, code = ex.Code });
        }
        catch (UsernameException ex) when (ex.Code == UsernameErrorCodes.Taken)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }
        catch (UsernameException ex)
        {
            return BadRequest(new { message = ex.Message, code = ex.Code });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Sends or resends an Apperture account setup invitation. Requires user.manage.</summary>
    [HttpPost("{id}/setup-invitation")]
    [ProducesResponseType(typeof(SetupInvitationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SetupInvitationResponse>> SendSetupInvitation(
        string id,
        [FromBody] SetupInvitationRequest? request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _userManagement.SendSetupInvitationAsync(new UserId(id), request?.ContactEmail, cancellationToken);
            return Ok(new SetupInvitationResponse(result.DeliverySucceeded, result.DeliveryFailure, result.ExpiresAtUtc));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (AccountSecurityException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Renames a user's username, or reclaims one of the user's own retired names. The target must satisfy the current
    /// username policy. Changes no credential, role or session; the Local sign-in name follows the username. Requires user.manage.
    /// </summary>
    [HttpPut("{id}/username")]
    [ProducesResponseType(typeof(RenameUsernameResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RenameUsernameResponse>> RenameUsername(
        string id,
        [FromBody] RenameUsernameRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request?.Username))
            return BadRequest(new { message = "Username is required.", code = UsernameErrorCodes.Invalid });

        try
        {
            var result = await _userManagement.RenameUsernameAsync(new UserId(id), request.Username, cancellationToken);
            return Ok(new RenameUsernameResponse(id, result.Username, result.Outcome.ToString().ToLowerInvariant()));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (EmergencyAccessException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }
        catch (UsernameException ex) when (ex.Code is UsernameErrorCodes.Taken or UsernameErrorCodes.ConcurrentChange)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }
        catch (UsernameException ex)
        {
            return BadRequest(new { message = ex.Message, code = ex.Code });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new { message = ex.Message });
        }
        catch (AuditOutcomeUnknownException ex)
        {
            // Neither commit nor rollback could be established (audit hardening design gate §13.2): never "failed" or
            // "unchanged". The client reloads and never resubmits automatically.
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "The rename may or may not have been applied. Refresh to confirm before trying again.",
                code = AuditOutcomeUnknownException.Code,
                operationEventId = ex.OperationEventId,
            });
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
        var sourceIsValid = Enum.TryParse<IdentitySource>(request.IdentitySource, ignoreCase: true, out var source) &&
            (source == IdentitySource.Entra || source == IdentitySource.Local);
        // Only Local may omit the name (it defaults to the user's username); every other source needs one.
        if (string.IsNullOrWhiteSpace(request.ExternalId) && (!sourceIsValid || source != IdentitySource.Local))
            return BadRequest(new IdentityLinkErrorResponse(
                "ExternalId is required.",
                IdentityLinkErrorCodes.MissingExternalId));
        if (!sourceIsValid)
            return BadRequest(new IdentityLinkErrorResponse(
                "IdentitySource must be 'Entra' or 'Local'.",
                IdentityLinkErrorCodes.InvalidSource));

        try
        {
            // A Local link's name is the user's username: omitted means "use it", supplied must equal it.
            var externalId = string.IsNullOrWhiteSpace(request.ExternalId) ? null : request.ExternalId.Trim();
            await _userManagement.AddIdentityLinkAsync(new UserId(id), source, externalId, cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (EmergencyAccessException ex)
        {
            return Conflict(new IdentityLinkErrorResponse(ex.Message, ex.Code));
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
        catch (UsernameException ex)
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
        catch (EmergencyAccessException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
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
        catch (EmergencyAccessException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
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
        catch (EmergencyAccessException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
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
        catch (EmergencyAccessException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
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
    [ProducesResponseType(StatusCodes.Status409Conflict)]
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
        catch (StrongAuthenticationException ex)
        {
            var status = ex.Code == StrongAuthenticationErrorCodes.NotConfigured
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status403Forbidden;
            return StatusCode(status, new IdentityLinkErrorResponse(ex.Message, ex.Code));
        }
        catch (EmergencyAccessException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
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
            user.Username,
            user.DisplayName,
            hasLocal,
            hasEntra,
            user.AssignedRoleIds.Select(r => r.Value).ToArray(),
            user.IsBreakGlassEligible);
    }

    private static UserDetailDto ToDetailDto(
        User user,
        bool? hasLocalCredential,
        bool? isCredentialDisabled,
        LocalAuthenticationPresentation? presentation = null,
        bool nativeMfaEnrolled = false,
        bool emergencyRecoveryCodesPresent = false,
        AccountInvitationStatus? invitation = null)
    {
        var hasLocalLink = user.IdentityLinks.Any(l => l.Source == IdentitySource.Local);
        var invitationState = ResolveInvitationState(invitation, hasLocalCredential);
        var passwordState = hasLocalCredential == true
            ? "set"
            : hasLocalLink ? "setup_pending" : "none";
        return new UserDetailDto(
            user.Id.Value,
            user.Username,
            user.DisplayName,
            user.IdentityLinks.Select(l => new IdentityLinkDto(l.Source.ToString(), l.ExternalId, l.LastAuthenticatedAt)).ToList(),
            user.AssignedRoleIds.Select(r => r.Value).ToList(),
            hasLocalCredential,
            isCredentialDisabled,
            presentation?.EntraAssociationStatus,
            presentation?.LocalSignInPolicy,
            presentation?.RecoverySafeguardActive ?? false,
            presentation?.EmergencyAccessAccount ?? user.IsBreakGlassEligible,
            nativeMfaEnrolled,
            emergencyRecoveryCodesPresent,
            user.ContactEmailStatus,
            user.EmailAddress,
            user.PendingEmailAddress,
            passwordState,
            invitationState);
    }

    private static string ResolveInvitationState(AccountInvitationStatus? invitation, bool? hasLocalCredential)
    {
        if (hasLocalCredential == true)
            return invitation is { Pending: false, Expired: false } ? "accepted" : "none";
        if (invitation?.Pending == true)
            return "pending";
        return invitation is null ? "none" : "expired";
    }
}

public sealed record UserListResponse(IReadOnlyList<UserListDto> Items, int TotalCount);

public sealed record UserListDto(
    string UserId,
    string Username,
    string DisplayName,
    bool HasLocalLink,
    bool HasEntraLink,
    string[] RoleIds,
    bool EmergencyAccessAccount = false);

public sealed record UserDetailDto(
    string UserId,
    string Username,
    string DisplayName,
    IReadOnlyList<IdentityLinkDto> IdentityLinks,
    IReadOnlyList<string> RoleIds,
    bool? HasLocalCredential,
    bool? IsCredentialDisabled,
    string? EntraAssociationStatus = null,
    string? LocalSignInPolicy = null,
    bool LocalRecoverySafeguard = false,
    bool EmergencyAccessAccount = false,
    bool NativeMfaEnrolled = false,
    bool EmergencyRecoveryCodesPresent = false,
    string ContactEmailStatus = "none",
    string? ContactEmail = null,
    string? PendingContactEmail = null,
    string PasswordState = "none",
    string InvitationState = "none");

public sealed record IdentityLinkDto(string Source, string ExternalId, DateTimeOffset? LastAuthenticatedAt);

/// <param name="ExternalId">
/// For Local, an accepted alias for <paramref name="Username"/> (the requested username); for Entra, the directory
/// object id. Optional for Local when <paramref name="Username"/> is supplied.
/// </param>
/// <param name="Username">
/// The preferred Local username. If both this and <paramref name="ExternalId"/> are supplied for Local they must
/// normalize to the same name (otherwise <c>username_mismatch</c>). Ignored for Entra.
/// </param>
public sealed record CreateUserRequest(
    string DisplayName,
    string IdentitySource,
    string? ExternalId = null,
    string? Password = null,
    string? ContactEmail = null,
    bool SendSetupInvitation = false,
    string? Username = null);

/// <param name="Username">The requested username. Normalized (trimmed, lower-cased) and validated against the current policy.</param>
public sealed record RenameUsernameRequest(string Username);

/// <param name="Username">The canonical stored username after the call.</param>
/// <param name="Outcome"><c>assigned</c> (a new name), <c>reclaimed</c> (one of the user's own retired names) or <c>unchanged</c>.</param>
public sealed record RenameUsernameResponse(string UserId, string Username, string Outcome);

public sealed record SetupInvitationRequest(string? ContactEmail);

public sealed record SetupInvitationResponse(bool DeliverySucceeded, string? DeliveryFailure, DateTimeOffset ExpiresAtUtc);

public sealed record CreateUserResponse(string UserId);

/// <param name="ExternalId">
/// Optional for Local, where it defaults to the user's username and must equal it when supplied. Required for Entra.
/// </param>
public sealed record AddIdentityLinkRequest(string IdentitySource, string? ExternalId = null);

public sealed record IdentityLinkErrorResponse(string Message, string Code);

public sealed record AssignRoleRequest(string RoleId);

public sealed record SetPasswordRequest(string? NewPassword);

public sealed record SetLocalCredentialDisabledRequest(bool IsDisabled);
