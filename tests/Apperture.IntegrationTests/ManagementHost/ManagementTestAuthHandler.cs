using System.Security.Claims;
using System.Text.Encodings.Web;
using Apperture.ControlPlane.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// Deterministic auth for Management Host integration tests. Maps <see cref="HeaderName"/> to a
/// <see cref="ClaimsPrincipal"/> that <see cref="ClaimsPrincipalToActorMapper"/> can resolve via SQL identity links.
/// </summary>
internal sealed class ManagementTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ManagementTest";
    public const string HeaderName = "X-Test-External-Id";

    public ManagementTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
            return Task.FromResult(AuthenticateResult.NoResult());

        var externalId = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(externalId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, externalId!),
            new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Local")
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
