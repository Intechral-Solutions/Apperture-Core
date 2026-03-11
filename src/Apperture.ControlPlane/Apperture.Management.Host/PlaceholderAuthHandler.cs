using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Apperture.Management.Host;

/// <summary>
/// Placeholder authentication handler for Epic 1. Never sets a user; allows auth pipeline to run.
/// Replace with Entra or local auth in a later epic. Marked as temporary scaffolding.
/// </summary>
public sealed class PlaceholderAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Placeholder";

    public PlaceholderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Never authenticate; HttpContext.User remains empty. Real schemes (Entra, local) replace this.
        return Task.FromResult(AuthenticateResult.NoResult());
    }
}
