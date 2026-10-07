using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Models;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Authentication endpoints: local login (username/password) and who-am-I. Microsoft sign-in (GET /signin) and every sign-out
/// (the UI's form POST to /signout) are handled by the host.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private const string AuthActionLocalLoginFailed = "auth.local.login.failed";
    private const string AuthActionUnlinkedPrincipal = "auth.unlinked_principal";
    private const string AuthActionBreakGlassLoginFailed = "auth.break_glass.login.failed";
    private const string AuthActionBreakGlassRejectedDisabled = "auth.break_glass.rejected_disabled";

    private const string PendingMfaCookieName = "apperture.mfa.pending";

    private readonly IUserRepository _userRepository;
    private readonly ILocalCredentialRepository _localCredentialRepository;
    private readonly ILocalPasswordHasher _passwordHasher;
    private readonly IPrincipalContext _principalContext;
    private readonly IPrincipalToUserResolver _principalToUserResolver;
    private readonly IRoleProvider _roleProvider;
    private readonly IUserPreferencesRepository _userPreferences;
    private readonly IAuditService _audit;
    private readonly LocalAuthOptions _localOptions;
    private readonly BreakGlassAuthOptions _breakGlassOptions;
    private readonly EntraAuthOptions _entraOptions;
    private readonly IInstanceAuthUiPolicyRepository _authUiPolicyRepository;
    private readonly IEntraPrimaryAuthenticationPolicy? _entraPrimaryPolicy;
    private readonly IAuthenticationAssuranceEvaluator? _assuranceEvaluator;
    private readonly INativeMfaService? _nativeMfa;
    private readonly IPendingMfaLoginTicketService? _pendingMfaTickets;
    private readonly IForgottenPasswordService? _forgottenPassword;
    private readonly IAccountInvitationService? _invitations;
    private readonly IAccountContactService? _contact;
    private readonly IAccountActionTokenService? _actionTokens;
    private readonly IRecentAccountVerificationGate? _accountVerification;
    private readonly ILogger<AuthController>? _logger;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAuditEventWriter? _auditEvents;

    public AuthController(
        IUserRepository userRepository,
        ILocalCredentialRepository localCredentialRepository,
        ILocalPasswordHasher passwordHasher,
        IPrincipalContext principalContext,
        IPrincipalToUserResolver principalToUserResolver,
        IRoleProvider roleProvider,
        IUserPreferencesRepository userPreferences,
        IAuditService audit,
        IOptions<LocalAuthOptions> localOptions,
        IOptions<BreakGlassAuthOptions> breakGlassOptions,
        IOptions<EntraAuthOptions> entraOptions,
        IInstanceAuthUiPolicyRepository authUiPolicyRepository,
        IEntraPrimaryAuthenticationPolicy? entraPrimaryPolicy = null,
        IAuthenticationAssuranceEvaluator? assuranceEvaluator = null,
        INativeMfaService? nativeMfa = null,
        IPendingMfaLoginTicketService? pendingMfaTickets = null,
        IForgottenPasswordService? forgottenPassword = null,
        IAccountInvitationService? invitations = null,
        IAccountContactService? contact = null,
        IAccountActionTokenService? actionTokens = null,
        IRecentAccountVerificationGate? accountVerification = null,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAuditEventWriter? auditEvents = null,
        ILogger<AuthController>? logger = null)
    {
        _logger = logger;
        _userRepository = userRepository;
        _localCredentialRepository = localCredentialRepository;
        _passwordHasher = passwordHasher;
        _principalContext = principalContext;
        _principalToUserResolver = principalToUserResolver;
        _roleProvider = roleProvider;
        _userPreferences = userPreferences;
        _audit = audit;
        _localOptions = localOptions.Value;
        _breakGlassOptions = breakGlassOptions.Value;
        _entraOptions = entraOptions.Value;
        _authUiPolicyRepository = authUiPolicyRepository;
        _entraPrimaryPolicy = entraPrimaryPolicy;
        _assuranceEvaluator = assuranceEvaluator;
        _nativeMfa = nativeMfa;
        _pendingMfaTickets = pendingMfaTickets;
        _forgottenPassword = forgottenPassword;
        _invitations = invitations;
        _contact = contact;
        _actionTokens = actionTokens;
        _accountVerification = accountVerification;
        _auditedUnitOfWork = auditedUnitOfWork;
        _auditEvents = auditEvents;
    }

    private IAuditedUnitOfWork AuditedUnit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Password changes require the audited unit of work.");

    private IAuditEventWriter AuditWriter =>
        _auditEvents ?? throw new InvalidOperationException("Sign-in and password changes require the audit event writer.");

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
        var userPrincipal = ResolveUserPrincipal(User) ?? user.Username;
        var localStatus = AccountMeSnapshotBuilder.ResolveLocalCredentialStatus(user, credential);
        var identityLinks = user.IdentityLinks
            .Select(l => new AccountIdentityLinkDto(
                l.Source.ToString(),
                AccountMeSnapshotBuilder.FormatIdentityLinkLabel(l.Source, l.ExternalId),
                l.LastAuthenticatedAt))
            .ToArray();
        // Preferences ride along on the session response so the shell can apply appearance and clock
        // format on first paint instead of after a second request.
        var preferences = await _userPreferences.GetAsync(uid, cancellationToken);
        var deferred = AccountMeSnapshotBuilder.BuildDeferredFeatures();
        var presentation = _entraPrimaryPolicy is null
            ? null
            : await _entraPrimaryPolicy.DescribeAsync(user, credential, cancellationToken);
        var assurance = _assuranceEvaluator?.Evaluate(User) ?? AuthenticationAssurance.None;
        var strongConfigured = (EntraConfigurationState.IsUsable(_entraOptions)
                && EntraAuthenticationContextId.TryNormalize(_entraOptions.AuthenticationContextId) is not null)
            || (await (_nativeMfa?.IsEnrolledAsync(uid, cancellationToken) ?? ValueTask.FromResult(false)));
        var nativeMfaEnrolled = _nativeMfa is not null && await _nativeMfa.IsEnrolledAsync(uid, cancellationToken);
        var accountVerification = _accountVerification?.GetCurrent() ?? RecentAccountVerification.None;
        return Ok(new MeResponse(
            user.Id.Value,
            user.Username,
            user.DisplayName,
            roles.Select(r => r.Id.Value).ToArray(),
            userPrincipal,
            AccountMeSnapshotBuilder.ResolveSignInSource(User),
            identityLinks,
            localStatus,
            roles.Select(r => r.Name).ToArray(),
            AccountMeSnapshotBuilder.CanChangeLocalPassword(_localOptions.Enabled, localStatus)
                && !user.IsBreakGlassEligible,
            deferred is null
                ? null
                : new AccountDeferredFeaturesDto(
                    deferred.AvatarManagementDeferred,
                    deferred.ProfileEditDeferred,
                    deferred.NotificationSubscriptionsDeferred,
                    deferred.Message),
            user.DisplayTimeZoneId,
            AccountAvatarDtoMapper.ToDto(user.EffectiveAvatar),
            preferences?.TimeFormat.ToString(),
            preferences?.Appearance.ToString(),
            presentation?.EntraAssociationStatus,
            presentation?.LocalSignInPolicy,
            assurance.RequirementSatisfied && assurance.IsRecent,
            strongConfigured,
            presentation?.EmergencyAccessAccount ?? user.IsBreakGlassEligible,
            nativeMfaEnrolled,
            accountVerification.IsSatisfied,
            accountVerification.ExpiresAtUtc));
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
            return await RefusePasswordChangeAsync(actor.Id.Value, "not_linked", Forbid());

        if (!_localOptions.Enabled)
        {
            return await RefusePasswordChangeAsync(userId.Value, "local_auth_disabled", BadRequest(new LocalPasswordChangeErrorResponse(
                "Local sign-in is disabled for this deployment.",
                LocalPasswordChangeErrorCodes.LocalAuthDisabled)));
        }

        if (request.NewPassword is null)
        {
            return await RefusePasswordChangeAsync(userId.Value, "missing_new_password", BadRequest(new LocalPasswordChangeErrorResponse(
                "A new password is required.",
                LocalPasswordChangeErrorCodes.InvalidNewPassword)));
        }

        // AUD-1.2: every check the change depends on is read in the transaction; the password, the revocation of the
        // account's password links and the row commit together. A refusal changes nothing and is a W3 row written after.
        var recentlyVerified = _accountVerification?.GetCurrent().IsSatisfied == true;
        (string Reason, IActionResult Response)? refusal = null;
        await AuditedUnit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.LocalPasswordChanged, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                refusal = await CheckPasswordChangeAsync(userId, request, recentlyVerified, ct);
                if (refusal is not null)
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                var (hash, salt) = _passwordHasher.HashPassword(request.NewPassword);
                await _localCredentialRepository.SetPasswordAsync(userId, hash, salt, _passwordHasher.AlgorithmName, ct);
                if (_actionTokens is not null)
                    await _actionTokens.RevokePasswordLifecycleTokensAsync(userId, ct);
                await transaction.StageAsync(AuditEvents.LocalPasswordChanged(userId), ct);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken);

        if (refusal is { } refused)
            return await RefusePasswordChangeAsync(userId.Value, refused.Reason, refused.Response);
        return NoContent();
    }

    /// <summary>The first reason the change is refused, read in the change's transaction; null when it may proceed.</summary>
    private async Task<(string Reason, IActionResult Response)?> CheckPasswordChangeAsync(
        UserId userId,
        ChangeLocalPasswordRequest request,
        bool recentlyVerified,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return ("user_not_found", Forbid());

        if (EmergencyAccessIdentity.IsSystemManaged(user))
        {
            return ("system_managed", Conflict(new LocalPasswordChangeErrorResponse(
                "This account is system-managed. Use Emergency Access operator tooling to change the password.",
                LocalPasswordChangeErrorCodes.NoCredential)));
        }

        if (!user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
        {
            return ("no_local_identity", BadRequest(new LocalPasswordChangeErrorResponse(
                "This account does not have a local sign-in credential.",
                LocalPasswordChangeErrorCodes.NoCredential)));
        }

        var credential = await _localCredentialRepository.GetByUserIdAsync(userId, cancellationToken);
        if (credential is null)
        {
            return ("no_credential", BadRequest(new LocalPasswordChangeErrorResponse(
                "This account does not have a local sign-in credential.",
                LocalPasswordChangeErrorCodes.NoCredential)));
        }

        if (credential.IsDisabled)
        {
            return ("credential_disabled", Conflict(new LocalPasswordChangeErrorResponse(
                "Local sign-in is disabled for this account. Contact an administrator.",
                LocalPasswordChangeErrorCodes.CredentialDisabled)));
        }

        if (!recentlyVerified)
        {
            if (string.IsNullOrEmpty(request.CurrentPassword))
            {
                return ("verification_required", StatusCode(
                    StatusCodes.Status403Forbidden,
                    new LocalPasswordChangeErrorResponse(
                        "Verify your identity to change your password.",
                        LocalPasswordChangeErrorCodes.RecentVerificationRequired)));
            }

            if (!_passwordHasher.VerifyPassword(request.CurrentPassword, credential.PasswordHash, credential.Salt))
            {
                return ("wrong_current_password", BadRequest(new LocalPasswordChangeErrorResponse(
                    "Current password is incorrect.",
                    LocalPasswordChangeErrorCodes.WrongCurrentPassword)));
            }
        }

        try
        {
            LocalPasswordPolicy.Require(request.NewPassword, user);
        }
        catch (PasswordPolicyException ex)
        {
            return ("policy_rejected", BadRequest(new LocalPasswordChangeErrorResponse(
                ex.Message,
                LocalPasswordChangeErrorCodes.InvalidNewPassword)));
        }

        return null;
    }

    /// <summary>Records a refused password change (W3, never changes the response) and returns the response.</summary>
    private async Task<IActionResult> RefusePasswordChangeAsync(string userId, string reason, IActionResult response)
    {
        await AuditWriter.TryWriteAsync(
            AuditEvents.LocalPasswordChangeRefused(userId, reason),
            AuditActor.CurrentPrincipal,
            CancellationToken.None);
        return response;
    }

    /// <summary>Unauthenticated forgotten-password request. Always returns the same acknowledgement.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ForgotPasswordResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (_forgottenPassword is not null)
        {
            try
            {
                await _forgottenPassword.RequestAsync(request.Identifier ?? "", cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Enumeration protection (AUD-1.2 review): a request that matches no eligible account changes nothing and
                // writes only a best-effort W3 row, so it answers with the acknowledgement even when the database refuses
                // writes. An eligible request whose change failed, or whose outcome is unknown, must answer the same way, or
                // the difference names the account. The acknowledgement never says a link was issued; the failure is logged.
                _logger?.LogError(
                    ex,
                    "A password reset request could not be completed (operation {OperationEventId}).",
                    (ex as AuditOutcomeUnknownException)?.OperationEventId ?? (ex as AuditOperationNotCompletedException)?.OperationEventId);
            }
        }

        return Ok(new ForgotPasswordResponse(ForgottenPasswordService.GenericAcknowledgement));
    }

    /// <summary>Consumes a one-time password-reset token. Does not enable a disabled local credential.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] TokenPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (_forgottenPassword is null)
            return Conflict(new { message = "This reset link is invalid or has expired.", code = AccountSecurityErrorCodes.TokenInvalid });
        try
        {
            await _forgottenPassword.CompleteAsync(request.Token ?? "", request.NewPassword ?? "", cancellationToken);
            return NoContent();
        }
        catch (AccountSecurityException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }
    }

    /// <summary>Consumes an Apperture account setup invitation and sets the first local password.</summary>
    [HttpPost("setup-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetupPassword(
        [FromBody] TokenPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (_invitations is null)
            return Conflict(new { message = "This setup link is invalid or has expired.", code = AccountSecurityErrorCodes.TokenInvalid });
        try
        {
            await _invitations.CompletePasswordSetupAsync(request.Token ?? "", request.NewPassword ?? "", cancellationToken);
            return NoContent();
        }
        catch (AccountSecurityException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }
    }

    /// <summary>Consumes a contact-email verification token.</summary>
    [HttpPost("verify-contact-email")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> VerifyContactEmail(
        [FromBody] VerifyContactEmailRequest request,
        CancellationToken cancellationToken)
    {
        if (_contact is null)
            return Conflict(new { message = "This verification link is invalid or has expired.", code = AccountSecurityErrorCodes.TokenInvalid });
        try
        {
            await _contact.VerifyAsync(request.Token ?? "", cancellationToken);
            return NoContent();
        }
        catch (AccountSecurityException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }
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

        var mfaRequired = _nativeMfa is not null && await _nativeMfa.IsRequiredAsync(user, cancellationToken);
        if (mfaRequired)
        {
            var enrolled = await _nativeMfa!.IsEnrolledAsync(user.Id, cancellationToken);
            if (!enrolled || _pendingMfaTickets is null)
            {
                await AppendAuthAuditAsync(
                    user.IsBreakGlassEligible ? EmergencyAccessAuditActionNames.LoginFailed : AuthActionLocalLoginFailed,
                    new ActorId("anonymous"),
                    user.IsBreakGlassEligible ? "mfa_not_enrolled" : null,
                    cancellationToken);
                return Unauthorized();
            }

            var ticket = await _pendingMfaTickets.IssueAsync(user.Id, DateTimeOffset.UtcNow, cancellationToken);
            Response.Cookies.Append(PendingMfaCookieName, ticket, CreatePendingMfaCookieOptions());
            return Ok(new LocalLoginResponse("", "", RequiresMfa: true));
        }

        // W2: the row commits before the session is issued; a failure to write it fails the request without a session.
        await RecordSignInAsync(AuditEvents.LocalSignInSucceeded(user.Id, user.IsBreakGlassEligible, "password"), user.Id, cancellationToken);
        await SignInLocalAsync(user, assuranceMechanism: null, cancellationToken);
        return Ok(new LocalLoginResponse(user.Id.Value, user.DisplayName));
    }

    /// <summary>
    /// Completes local sign-in after a successful password when native MFA is required.
    /// Accepts a TOTP code or a one-time recovery code. Do not reveal whether the username exists.
    /// </summary>
    [HttpPost("local/mfa")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LocalLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LocalLoginResponse>> LocalMfa(
        [FromBody] LocalMfaChallengeRequest request,
        CancellationToken cancellationToken)
    {
        if (!_localOptions.Enabled || _nativeMfa is null || _pendingMfaTickets is null)
        {
            await AppendAuthAuditAsync(AuthActionLocalLoginFailed, new ActorId("anonymous"), "MFA challenge unavailable", cancellationToken);
            return Unauthorized();
        }

        var cookie = Request.Cookies[PendingMfaCookieName];
        var ticket = cookie is null ? null : await _pendingMfaTickets.RedeemAsync(cookie, DateTimeOffset.UtcNow, cancellationToken);
        if (ticket is null)
        {
            ClearPendingMfaCookie();
            await AppendAuthAuditAsync(AuthActionLocalLoginFailed, new ActorId("anonymous"), "MFA ticket invalid", cancellationToken);
            return Unauthorized();
        }

        var user = await _userRepository.GetByIdAsync(ticket.UserId, cancellationToken);
        if (user is null)
        {
            ClearPendingMfaCookie();
            await AppendAuthAuditAsync(AuthActionLocalLoginFailed, new ActorId("anonymous"), null, cancellationToken);
            return Unauthorized();
        }

        var totp = request.TotpCode;
        var recovery = request.RecoveryCode;
        var hasTotp = !string.IsNullOrWhiteSpace(totp);
        var hasRecovery = !string.IsNullOrWhiteSpace(recovery);
        if (hasTotp == hasRecovery)
        {
            await AppendAuthAuditAsync(
                user.IsBreakGlassEligible ? EmergencyAccessAuditActionNames.LoginFailed : AuthActionLocalLoginFailed,
                new ActorId("anonymous"),
                "mfa_challenge_invalid",
                cancellationToken);
            return Unauthorized();
        }

        string mechanism;
        if (hasTotp)
        {
            if (!await _nativeMfa.VerifyTotpAsync(user.Id, totp!, cancellationToken))
            {
                await AppendAuthAuditAsync(
                    user.IsBreakGlassEligible ? EmergencyAccessAuditActionNames.LoginFailed : AuthActionLocalLoginFailed,
                    new ActorId("anonymous"),
                    "totp_invalid",
                    cancellationToken);
                return Unauthorized();
            }

            mechanism = AuthenticationAssuranceClaims.NativeTotpMechanism;
        }
        else
        {
            if (!await _nativeMfa.ConsumeRecoveryCodeAsync(user.Id, recovery!, cancellationToken))
            {
                await AppendAuthAuditAsync(
                    user.IsBreakGlassEligible ? EmergencyAccessAuditActionNames.LoginFailed : AuthActionLocalLoginFailed,
                    new ActorId("anonymous"),
                    "recovery_code_invalid",
                    cancellationToken);
                return Unauthorized();
            }

            mechanism = AuthenticationAssuranceClaims.NativeRecoveryCodeMechanism;
        }

        // W2 before the session (D4: a consumed recovery code stays consumed if this write fails; the user signs in again).
        await RecordSignInAsync(
            AuditEvents.LocalSignInSucceeded(user.Id, user.IsBreakGlassEligible, hasTotp ? "native_totp" : "native_recovery_code"),
            user.Id,
            cancellationToken);
        ClearPendingMfaCookie();
        await SignInLocalAsync(user, mechanism, cancellationToken);
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

        // The ordinary local session identity (user id), plus the legacy break-glass marker.
        var identity = LocalSessionIdentity.Create(user.Id, user.Username, user.DisplayName);
        identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.BreakGlassClaimType, "1"));

        var principal = new ClaimsPrincipal(identity);
        var props = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
        };

        // W2: the row commits before the session is issued.
        await RecordSignInAsync(AuditEvents.BreakGlassSignInSucceeded(user.Id), user.Id, cancellationToken);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);
        return Ok(new LocalLoginResponse(user.Id.Value, user.DisplayName));
    }

    /// <summary>
    /// Writes a sign-in's W2 row as the user this request just authenticated (D2). It commits before the caller issues the
    /// session; when it cannot be written this throws and no session is issued.
    /// </summary>
    private Task RecordSignInAsync(Apperture.Core.Application.Audit.AuditEvent signedIn, UserId userId, CancellationToken cancellationToken) =>
        AuditWriter.WriteAsync(signedIn, AuditActor.AuthenticatedUser(userId), cancellationToken);

    private async Task SignInLocalAsync(
        User user,
        string? assuranceMechanism,
        CancellationToken cancellationToken)
    {
        var identity = LocalSessionIdentity.Create(user.Id, user.Username, user.DisplayName);
        if (!string.IsNullOrWhiteSpace(assuranceMechanism))
        {
            AuthenticationAssuranceClaims.Replace(identity, assuranceMechanism, DateTimeOffset.UtcNow, providerContext: null);
        }

        var principal = new ClaimsPrincipal(identity);
        var props = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(LocalSessionIdentity.Lifetime)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);
        _ = cancellationToken;
    }

    private CookieOptions CreatePendingMfaCookieOptions()
    {
        var environment = HttpContext.RequestServices.GetService<IHostEnvironment>();
        return new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = environment is not null && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"),
            MaxAge = PendingMfaLoginTicketService.Lifetime
        };
    }

    private void ClearPendingMfaCookie()
    {
        Response.Cookies.Delete(PendingMfaCookieName, CreatePendingMfaCookieOptions());
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
    /// The sign-in name an Entra token carries. Deliberately not the session cookie's username claim: that is frozen
    /// at sign-in and goes stale on rename, so callers fall back to the user's current username instead.
    /// </summary>
    private static string? ResolveUserPrincipal(ClaimsPrincipal principal)
    {
        return principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst("upn")?.Value
            ?? principal.FindFirst(ClaimTypes.Upn)?.Value
            ?? principal.FindFirst("email")?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.FindFirst("unique_name")?.Value;
    }
}

public sealed record LocalLoginRequest(string Username, string? Password);

public sealed record LocalLoginResponse(string UserId, string DisplayName, bool RequiresMfa = false);

public sealed record LocalMfaChallengeRequest(string? TotpCode, string? RecoveryCode);

public sealed record MeResponse(
    string UserId,
    /// <summary>The user's current canonical username, read from the user record on every request.</summary>
    string Username,
    string DisplayName,
    string[] Roles,
    string? UserPrincipal,
    string SignInSource,
    IReadOnlyList<AccountIdentityLinkDto> IdentityLinks,
    string LocalCredentialStatus,
    string[] RoleNames,
    bool CanChangeLocalPassword,
    /// <summary>Account capabilities not yet available, or null when there are none.</summary>
    AccountDeferredFeaturesDto? Deferred,
    /// <summary>Stored operator display timezone preference; null means follow the browser.</summary>
    string? DisplayTimeZoneId = null,
    /// <summary>Current avatar selection, so the shell can render it without a second request.</summary>
    Apperture.Management.Client.AccountAvatarDto? Avatar = null,
    /// <summary>Stored clock preference (<c>System</c>, <c>Hour12</c>, <c>Hour24</c>).</summary>
    string? TimeFormat = null,
    /// <summary>Stored appearance preference (<c>System</c>, <c>Light</c>, <c>Dark</c>); null when unset.</summary>
    string? Appearance = null,
    string? EntraAssociationStatus = null,
    string? LocalSignInPolicy = null,
    bool HasRecentStrongAuthentication = false,
    bool StrongAuthenticationConfigured = false,
    bool EmergencyAccessAccount = false,
    bool NativeMfaEnrolled = false,
    bool HasRecentAccountVerification = false,
    DateTimeOffset? RecentAccountVerificationExpiresAtUtc = null);

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

public sealed record ForgotPasswordRequest(string? Identifier);

public sealed record ForgotPasswordResponse(string Message);

public sealed record TokenPasswordRequest(string? Token, string? NewPassword);

public sealed record VerifyContactEmailRequest(string? Token);

/// <summary>Stable self-service password change error codes for clients.</summary>
public static class LocalPasswordChangeErrorCodes
{
    public const string WrongCurrentPassword = "auth.local_password.wrong_current";
    public const string NoCredential = "auth.local_password.no_credential";
    public const string CredentialDisabled = "auth.local_password.credential_disabled";
    public const string InvalidNewPassword = "auth.local_password.invalid_new";
    public const string LocalAuthDisabled = "auth.local_password.local_auth_disabled";
    public const string RecentVerificationRequired = "auth.account_verification.required";
}

public sealed record LocalPasswordChangeErrorResponse(string Message, string Code);

/// <summary>Stable break-glass error codes for clients and automation.</summary>
public static class BreakGlassAuthErrorCodes
{
    public const string Disabled = "auth.break_glass.disabled";
}

/// <summary>Structured break-glass error payload.</summary>
public sealed record BreakGlassAuthErrorResponse(string Message, string Code);
