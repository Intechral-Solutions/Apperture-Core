using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Models;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Authentication endpoints: local login (username/password), who-am-I, and local logout. Sign-in/sign-out for Entra are handled by the host (GET /signin, /signout).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private const string AuthActionLocalLoginSuccess = "auth.local.login.success";
    private const string AuthActionLocalLoginFailed = "auth.local.login.failed";
    private const string AuthActionUnlinkedPrincipal = "auth.unlinked_principal";
    private const string AuthActionBreakGlassLoginSuccess = "auth.break_glass.login.success";
    private const string AuthActionBreakGlassLoginFailed = "auth.break_glass.login.failed";
    private const string AuthActionBreakGlassRejectedDisabled = "auth.break_glass.rejected_disabled";
    private const string AuthActionLocalPasswordChangeSuccess = "auth.local.password_change.success";
    private const string AuthActionLocalPasswordChangeFailed = "auth.local.password_change.failed";

    private readonly IUserRepository _userRepository;
    private readonly ILocalCredentialRepository _localCredentialRepository;
    private readonly ILocalPasswordHasher _passwordHasher;
    private readonly IPrincipalContext _principalContext;
    private readonly IPrincipalToUserResolver _principalToUserResolver;
    private readonly IRoleProvider _roleProvider;
    private readonly IAuditService _audit;
    private readonly LocalAuthOptions _localOptions;
    private readonly BreakGlassAuthOptions _breakGlassOptions;
    private readonly EntraAuthOptions _entraOptions;
    private readonly IInstanceAuthUiPolicyRepository _authUiPolicyRepository;

    public AuthController(
        IUserRepository userRepository,
        ILocalCredentialRepository localCredentialRepository,
        ILocalPasswordHasher passwordHasher,
        IPrincipalContext principalContext,
        IPrincipalToUserResolver principalToUserResolver,
        IRoleProvider roleProvider,
        IAuditService audit,
        IOptions<LocalAuthOptions> localOptions,
        IOptions<BreakGlassAuthOptions> breakGlassOptions,
        IOptions<EntraAuthOptions> entraOptions,
        IInstanceAuthUiPolicyRepository authUiPolicyRepository)
    {
        _userRepository = userRepository;
        _localCredentialRepository = localCredentialRepository;
        _passwordHasher = passwordHasher;
        _principalContext = principalContext;
        _principalToUserResolver = principalToUserResolver;
        _roleProvider = roleProvider;
        _audit = audit;
        _localOptions = localOptions.Value;
        _breakGlassOptions = breakGlassOptions.Value;
        _entraOptions = entraOptions.Value;
        _authUiPolicyRepository = authUiPolicyRepository;
    }

    /// <summary>Merged anonymous-safe sign-in capabilities and chooser policy for the management UI.</summary>
    [HttpGet("sign-in-options")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthSignInOptionsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthSignInOptionsResponse>> GetSignInOptions(CancellationToken cancellationToken)
    {
        var policy = await _authUiPolicyRepository.GetAsync(cancellationToken);
        var built = AuthSignInSnapshotBuilder.Build(_localOptions, _entraOptions, policy);
        var snapshot = built.Snapshot;
        return Ok(new AuthSignInOptionsResponse
        {
            LocalSignInEnabled = snapshot.LocalSignInEnabled,
            EntraSignInEnabled = snapshot.EntraSignInEnabled,
            EntraMisconfigured = snapshot.EntraMisconfigured,
            ShowLocalSignInInPrimaryChooser = snapshot.ShowLocalSignInInPrimaryChooser,
            PreferEntraInChooser = snapshot.PreferEntraInChooser,
            AutoRedirectToEntra = snapshot.AutoRedirectToEntra,
            EffectiveExperience = built.EffectiveExperience.ToString(),
            EffectiveExperienceDescription = AuthSignInEffectiveExperience.Describe(built.EffectiveExperience)
        });
    }

    /// <summary>
    /// Returns current user info (userId, displayName, roles). Requires [Authorize]. Returns 403 if authenticated but not linked to a platform user.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return Unauthorized();

        var userId = await _principalToUserResolver.ResolveUserIdAsync(actor, cancellationToken);
        if (userId is null)
        {
            await AppendAuthAuditAsync(AuthActionUnlinkedPrincipal, new ActorId(actor.Id.Value), "Authenticated principal not linked to platform user", cancellationToken);
            return Forbid();
        }

        var uid = userId!;
        var user = await _userRepository.GetByIdAsync(uid, cancellationToken);
        if (user is null)
            return Forbid();

        var roles = await _roleProvider.GetRolesForUserAsync(uid, cancellationToken);
        var credential = await _localCredentialRepository.GetByUserIdAsync(uid, cancellationToken);
        var userPrincipal = ResolveUserPrincipal(User);
        var localStatus = AccountMeSnapshotBuilder.ResolveLocalCredentialStatus(user, credential);
        var identityLinks = user.IdentityLinks
            .Select(l => new AccountIdentityLinkDto(
                l.Source.ToString(),
                AccountMeSnapshotBuilder.FormatIdentityLinkLabel(l.Source, l.ExternalId),
                l.LastAuthenticatedAt))
            .ToArray();
        var deferred = AccountMeSnapshotBuilder.BuildDeferredFeatures();
        return Ok(new MeResponse(
            user.Id.Value,
            user.DisplayName,
            roles.Select(r => r.Id.Value).ToArray(),
            userPrincipal,
            AccountMeSnapshotBuilder.ResolveSignInSource(User),
            identityLinks,
            localStatus,
            roles.Select(r => r.Name).ToArray(),
            AccountMeSnapshotBuilder.CanChangeLocalPassword(_localOptions.Enabled, localStatus),
            new AccountDeferredFeaturesDto(
                deferred.AvatarManagementDeferred,
                deferred.ProfileEditDeferred,
                deferred.NotificationSubscriptionsDeferred,
                deferred.Message)));
    }

    /// <summary>
    /// Self-service local password change for the signed-in user when an active local credential exists.
    /// </summary>
    [HttpPost("local/change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(LocalPasswordChangeErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(LocalPasswordChangeErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeLocalPassword(
        [FromBody] ChangeLocalPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return Unauthorized();

        var userId = await _principalToUserResolver.ResolveUserIdAsync(actor, cancellationToken);
        if (userId is null)
        {
            await AppendAuthAuditAsync(
                AuthActionLocalPasswordChangeFailed,
                new ActorId(actor.Id.Value),
                "Authenticated principal not linked to platform user",
                cancellationToken);
            return Forbid();
        }

        if (!_localOptions.Enabled)
        {
            await AppendAuthAuditAsync(
                AuthActionLocalPasswordChangeFailed,
                new ActorId(userId.Value),
                "Local auth disabled",
                cancellationToken);
            return BadRequest(new LocalPasswordChangeErrorResponse(
                "Local sign-in is disabled for this deployment.",
                LocalPasswordChangeErrorCodes.LocalAuthDisabled));
        }

        if (request.CurrentPassword is null || request.NewPassword is null)
        {
            await AppendAuthAuditAsync(
                AuthActionLocalPasswordChangeFailed,
                new ActorId(userId.Value),
                "Missing password fields",
                cancellationToken);
            return BadRequest(new LocalPasswordChangeErrorResponse(
                "Current and new password are required.",
                LocalPasswordChangeErrorCodes.InvalidNewPassword));
        }

        if (_localOptions.MinPasswordLength > 0 && request.NewPassword.Length < _localOptions.MinPasswordLength)
        {
            await AppendAuthAuditAsync(
                AuthActionLocalPasswordChangeFailed,
                new ActorId(userId.Value),
                $"New password shorter than minimum ({_localOptions.MinPasswordLength})",
                cancellationToken);
            return BadRequest(new LocalPasswordChangeErrorResponse(
                $"Password must be at least {_localOptions.MinPasswordLength} characters.",
                LocalPasswordChangeErrorCodes.InvalidNewPassword));
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            await AppendAuthAuditAsync(
                AuthActionLocalPasswordChangeFailed,
                new ActorId(userId.Value),
                "Platform user not found",
                cancellationToken);
            return Forbid();
        }

        if (!user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
        {
            await AppendAuthAuditAsync(
                AuthActionLocalPasswordChangeFailed,
                new ActorId(userId.Value),
                "No local identity link",
                cancellationToken);
            return BadRequest(new LocalPasswordChangeErrorResponse(
                "This account does not have a local sign-in credential.",
                LocalPasswordChangeErrorCodes.NoCredential));
        }

        var credential = await _localCredentialRepository.GetByUserIdAsync(userId, cancellationToken);
        if (credential is null)
        {
            await AppendAuthAuditAsync(
                AuthActionLocalPasswordChangeFailed,
                new ActorId(userId.Value),
                "No local credential row",
                cancellationToken);
            return BadRequest(new LocalPasswordChangeErrorResponse(
                "This account does not have a local sign-in credential.",
                LocalPasswordChangeErrorCodes.NoCredential));
        }

        if (credential.IsDisabled)
        {
            await AppendAuthAuditAsync(
                AuthActionLocalPasswordChangeFailed,
                new ActorId(userId.Value),
                "Local credential disabled",
                cancellationToken);
            return Conflict(new LocalPasswordChangeErrorResponse(
                "Local sign-in is disabled for this account. Contact an administrator.",
                LocalPasswordChangeErrorCodes.CredentialDisabled));
        }

        if (!_passwordHasher.VerifyPassword(request.CurrentPassword, credential.PasswordHash, credential.Salt))
        {
            await AppendAuthAuditAsync(
                AuthActionLocalPasswordChangeFailed,
                new ActorId(userId.Value),
                "Current password verification failed",
                cancellationToken);
            return BadRequest(new LocalPasswordChangeErrorResponse(
                "Current password is incorrect.",
                LocalPasswordChangeErrorCodes.WrongCurrentPassword));
        }

        var (hash, salt) = _passwordHasher.HashPassword(request.NewPassword);
        await _localCredentialRepository.SetPasswordAsync(
            userId,
            hash,
            salt,
            _passwordHasher.AlgorithmName,
            cancellationToken);
        await AppendAuthAuditAsync(
            AuthActionLocalPasswordChangeSuccess,
            new ActorId(userId.Value),
            null,
            cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Local login: validate username/password, sign in with cookie. Returns 401 for invalid or disabled; 200 with user info on success.
    /// Username is normalized (trim, lowercase) before lookup. Do not reveal whether the username exists.
    /// </summary>
    [HttpPost("local/login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LocalLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LocalLoginResponse>> LocalLogin(
        [FromBody] LocalLoginRequest request,
        CancellationToken cancellationToken)
    {
        if (!_localOptions.Enabled)
        {
            await AppendAuthAuditAsync(AuthActionLocalLoginFailed, new ActorId("anonymous"), "Local auth disabled", cancellationToken);
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Username) || request.Password is null)
        {
            await AppendAuthAuditAsync(AuthActionLocalLoginFailed, new ActorId("anonymous"), "Missing credentials", cancellationToken);
            return Unauthorized();
        }

        var normalizedUsername = LocalAuthHelpers.NormalizeUsername(request.Username);
        var user = await _userRepository.GetByIdentityLinkAsync(IdentitySource.Local, normalizedUsername, cancellationToken);
        if (user is null)
        {
            await AppendAuthAuditAsync(AuthActionLocalLoginFailed, new ActorId("anonymous"), null, cancellationToken);
            return Unauthorized();
        }

        var credential = await _localCredentialRepository.GetByUserIdAsync(user.Id, cancellationToken);
        if (credential is null || credential.IsDisabled)
        {
            await AppendAuthAuditAsync(AuthActionLocalLoginFailed, new ActorId("anonymous"), null, cancellationToken);
            return Unauthorized();
        }

        if (!_passwordHasher.VerifyPassword(request.Password, credential.PasswordHash, credential.Salt))
        {
            await AppendAuthAuditAsync(AuthActionLocalLoginFailed, new ActorId("anonymous"), null, cancellationToken);
            return Unauthorized();
        }

        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, normalizedUsername));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName));
        identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Local"));

        var principal = new ClaimsPrincipal(identity);
        var props = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);

        await AppendAuthAuditAsync(AuthActionLocalLoginSuccess, new ActorId(user.Id.Value), null, cancellationToken);
        return Ok(new LocalLoginResponse(user.Id.Value, user.DisplayName));
    }

    /// <summary>
    /// Break-glass login: local-auth-only recovery path. Disabled by default for v1 (<see cref="BreakGlassAuthOptions.Enabled"/>).
    /// When enabled, requires <see cref="User.IsBreakGlassEligible"/> and grants all permission checks for the session.
    /// </summary>
    [HttpPost("break-glass")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LocalLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BreakGlassAuthErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LocalLoginResponse>> BreakGlassLogin(
        [FromBody] LocalLoginRequest request,
        CancellationToken cancellationToken)
    {
        if (!_breakGlassOptions.Enabled)
        {
            await AppendAuthAuditAsync(
                AuthActionBreakGlassRejectedDisabled,
                new ActorId("anonymous"),
                "Break-glass login is not available.",
                cancellationToken);
            return NotFound(new BreakGlassAuthErrorResponse(
                "Break-glass login is not available in this release.",
                BreakGlassAuthErrorCodes.Disabled));
        }

        if (!_localOptions.Enabled)
        {
            await AppendAuthAuditAsync(AuthActionBreakGlassLoginFailed, new ActorId("anonymous"), null, cancellationToken);
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Username) || request.Password is null)
        {
            await AppendAuthAuditAsync(AuthActionBreakGlassLoginFailed, new ActorId("anonymous"), null, cancellationToken);
            return Unauthorized();
        }

        var normalizedUsername = LocalAuthHelpers.NormalizeUsername(request.Username);
        var user = await _userRepository.GetByIdentityLinkAsync(IdentitySource.Local, normalizedUsername, cancellationToken);
        if (user is null)
        {
            await AppendAuthAuditAsync(AuthActionBreakGlassLoginFailed, new ActorId("anonymous"), null, cancellationToken);
            return Unauthorized();
        }

        var credential = await _localCredentialRepository.GetByUserIdAsync(user.Id, cancellationToken);
        if (credential is null || credential.IsDisabled)
        {
            await AppendAuthAuditAsync(AuthActionBreakGlassLoginFailed, new ActorId("anonymous"), null, cancellationToken);
            return Unauthorized();
        }

        if (!_passwordHasher.VerifyPassword(request.Password, credential.PasswordHash, credential.Salt))
        {
            await AppendAuthAuditAsync(AuthActionBreakGlassLoginFailed, new ActorId("anonymous"), null, cancellationToken);
            return Unauthorized();
        }

        if (!user.IsBreakGlassEligible)
        {
            await AppendAuthAuditAsync(AuthActionBreakGlassLoginFailed, new ActorId("anonymous"), null, cancellationToken);
            return Unauthorized();
        }

        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, normalizedUsername));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName));
        identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Local"));
        identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.BreakGlassClaimType, "1"));

        var principal = new ClaimsPrincipal(identity);
        var props = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);

        await AppendAuthAuditAsync(AuthActionBreakGlassLoginSuccess, new ActorId(user.Id.Value), null, cancellationToken);
        return Ok(new LocalLoginResponse(user.Id.Value, user.DisplayName));
    }

    private async Task AppendAuthAuditAsync(string action, ActorId actorId, string? details, CancellationToken cancellationToken)
    {
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actorId,
            action,
            Resource: null,
            Outcome: "auth",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            details);
        await _audit.AppendAsync(record, cancellationToken);
    }

    /// <summary>
    /// Optional: clear cookie for local sign-out (same as signout for cookie).
    /// </summary>
    [HttpPost("local/logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LocalLogout(CancellationToken cancellationToken)
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    private static string? ResolveUserPrincipal(ClaimsPrincipal principal)
    {
        return principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst("upn")?.Value
            ?? principal.FindFirst(ClaimTypes.Upn)?.Value
            ?? principal.FindFirst("email")?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.FindFirst("unique_name")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}

public sealed record LocalLoginRequest(string Username, string? Password);

public sealed record LocalLoginResponse(string UserId, string DisplayName);

public sealed record MeResponse(
    string UserId,
    string DisplayName,
    string[] Roles,
    string? UserPrincipal,
    string SignInSource,
    IReadOnlyList<AccountIdentityLinkDto> IdentityLinks,
    string LocalCredentialStatus,
    string[] RoleNames,
    bool CanChangeLocalPassword,
    AccountDeferredFeaturesDto Deferred);

public sealed record AccountIdentityLinkDto(
    string Source,
    string DisplayLabel,
    DateTimeOffset? LastAuthenticatedAtUtc);

public sealed record AccountDeferredFeaturesDto(
    bool AvatarManagementDeferred,
    bool ProfileEditDeferred,
    bool NotificationSubscriptionsDeferred,
    string Message);

public sealed record ChangeLocalPasswordRequest(string? CurrentPassword, string? NewPassword);

/// <summary>Stable self-service password change error codes for clients.</summary>
public static class LocalPasswordChangeErrorCodes
{
    public const string WrongCurrentPassword = "auth.local_password.wrong_current";
    public const string NoCredential = "auth.local_password.no_credential";
    public const string CredentialDisabled = "auth.local_password.credential_disabled";
    public const string InvalidNewPassword = "auth.local_password.invalid_new";
    public const string LocalAuthDisabled = "auth.local_password.local_auth_disabled";
}

public sealed record LocalPasswordChangeErrorResponse(string Message, string Code);

/// <summary>Stable break-glass error codes for clients and automation.</summary>
public static class BreakGlassAuthErrorCodes
{
    public const string Disabled = "auth.break_glass.disabled";
}

/// <summary>Structured break-glass error payload.</summary>
public sealed record BreakGlassAuthErrorResponse(string Message, string Code);
