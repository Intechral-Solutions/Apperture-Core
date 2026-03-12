using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
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

    private readonly IUserRepository _userRepository;
    private readonly ILocalCredentialRepository _localCredentialRepository;
    private readonly ILocalPasswordHasher _passwordHasher;
    private readonly IPrincipalContext _principalContext;
    private readonly IPrincipalToUserResolver _principalToUserResolver;
    private readonly IRoleProvider _roleProvider;
    private readonly IAuditService _audit;
    private readonly LocalAuthOptions _localOptions;

    public AuthController(
        IUserRepository userRepository,
        ILocalCredentialRepository localCredentialRepository,
        ILocalPasswordHasher passwordHasher,
        IPrincipalContext principalContext,
        IPrincipalToUserResolver principalToUserResolver,
        IRoleProvider roleProvider,
        IAuditService audit,
        IOptions<LocalAuthOptions> localOptions)
    {
        _userRepository = userRepository;
        _localCredentialRepository = localCredentialRepository;
        _passwordHasher = passwordHasher;
        _principalContext = principalContext;
        _principalToUserResolver = principalToUserResolver;
        _roleProvider = roleProvider;
        _audit = audit;
        _localOptions = localOptions.Value;
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
        return Ok(new MeResponse(user.Id.Value, user.DisplayName, roles.Select(r => r.Id.Value).ToArray()));
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
}

public sealed record LocalLoginRequest(string Username, string? Password);

public sealed record LocalLoginResponse(string UserId, string DisplayName);

public sealed record MeResponse(string UserId, string DisplayName, string[] Roles);
