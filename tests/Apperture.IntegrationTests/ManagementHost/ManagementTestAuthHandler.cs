using System.Security.Claims;
using System.Text.Encodings.Web;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// Deterministic auth for Management Host integration tests. Turns <see cref="HeaderName"/> (a Local username, or
/// an Entra object id with <see cref="IdentitySourceHeaderName"/> and <see cref="TenantIdHeaderName"/>) into an
/// application session in the shape the host issues (<see cref="ApplicationSessionClaims"/>): the key is looked up
/// through its identity link, as sign-in does, and the session carries that user's id. An Entra session also
/// carries its tid and oid as link provenance. A key with no link still authenticates, as a session whose user id
/// does not exist, so the account-unavailable paths stay testable.
/// </summary>
/// <remarks>
/// <see cref="UserIdHeaderName"/> pins the session's user id instead of looking it up, to model a session issued
/// earlier: the resolver's link binding then decides whether it still stands.
/// </remarks>
internal sealed class ManagementTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ManagementTest";
    public const string HeaderName = "X-Test-External-Id";
    public const string IdentitySourceHeaderName = "X-Test-Identity-Source";
    public const string TenantIdHeaderName = "X-Test-Tenant-Id";
    public const string UserIdHeaderName = "X-Test-User-Id";

    public ManagementTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var externalId = Header(HeaderName);
        if (externalId is null)
            return AuthenticateResult.NoResult();

        var isEntra = string.Equals(Header(IdentitySourceHeaderName), ApplicationSessionClaims.EntraSource, StringComparison.OrdinalIgnoreCase);
        var tenantId = Header(TenantIdHeaderName);

        var users = Context.RequestServices.GetRequiredService<IUserRepository>();
        User? user = null;
        if (isEntra)
        {
            if (EntraTokenClaims.IsValidObjectId(tenantId) && EntraTokenClaims.IsValidObjectId(externalId))
                user = await users.GetByIdentityLinkAsync(ProviderIdentityKey.Entra(tenantId!, externalId), Context.RequestAborted);
        }
        else
        {
            user = await users.GetByIdentityLinkAsync(IdentitySource.Local, externalId, Context.RequestAborted);
        }

        var userId = Header(UserIdHeaderName) ?? user?.Id.Value ?? "unlinked-" + externalId;
        var identity = new ClaimsIdentity(SchemeName);
        ApplicationSessionClaims.Apply(
            identity,
            new UserId(userId),
            user?.Username ?? externalId,
            user?.DisplayName ?? externalId,
            isEntra ? ApplicationSessionClaims.EntraSource : ApplicationSessionClaims.LocalSource);
        if (isEntra)
        {
            identity.AddClaim(new Claim(EntraTokenClaims.OidClaim, externalId));
            if (tenantId is not null)
                identity.AddClaim(new Claim(EntraTokenClaims.TidClaim, tenantId));
        }

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    private string? Header(string name)
    {
        var value = Request.Headers.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
