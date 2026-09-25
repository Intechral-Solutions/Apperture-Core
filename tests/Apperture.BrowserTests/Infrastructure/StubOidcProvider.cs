using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>Fixed, non-secret identity of the stub provider (an Entra-shaped authority on 127.0.0.1).</summary>
public sealed record StubOidcSettings(string Origin)
{
    /// <summary>Directory (tenant) id the stub issues tokens for. A fixed, well-formed GUID.</summary>
    public string TenantId => "5b1c2d3e-4f50-4a61-8b72-9c83d4e5f607";

    public string ClientId => "apperture-browser-tests-client";

    /// <summary>Not a credential: the stub accepts any value. It exists only so Entra counts as "configured".</summary>
    public string ClientSecret => "stub-client-secret-not-a-credential";

    public string AuthenticationContextId => "c1";

    public string Authority => $"{Origin}/{TenantId}/v2.0";
}

/// <summary>What the stub provider answers at its authorize endpoint.</summary>
public enum StubOidcBehavior
{
    /// <summary>Issue a code; the token endpoint then returns a signed id_token.</summary>
    Succeed,

    /// <summary>The user cancelled or denied consent: <c>error=access_denied</c>.</summary>
    AccessDenied,

    /// <summary>The provider failed: <c>error=server_error</c>.</summary>
    ProviderError,
}

/// <summary>Identity the stub places in the next id_token it issues.</summary>
public sealed record StubOidcIdentity(string ObjectId, string DisplayName, bool IncludeAuthenticationContext, string? TenantId = null);

/// <summary>One authorize request as the stub saw it. No code, token or secret is ever stored.</summary>
public sealed record StubAuthorizeRequest(
    int Sequence,
    string ResponseMode,
    string RedirectUri,
    string? Prompt,
    bool HasClaimsParameter,
    bool HasStateParameter,
    StubOidcBehavior Behavior);

/// <summary>
/// A local OpenID Connect provider on <c>http://127.0.0.1:{port}</c>. It stands in for Entra: discovery
/// document, JWKS, an authorize endpoint that can succeed / cancel / fail (answering with a browser-driven
/// <c>form_post</c>, exactly how Entra returns the code), and a token endpoint that signs id_tokens with an
/// in-memory RSA key. The host under test does the real OpenID Connect handshake against it.
/// <para>
/// Cross-site by construction: the application lives on <c>localhost</c>, the provider on the IP literal
/// <c>127.0.0.1</c>, which every browser treats as a different site, so the provider's POST back to
/// <c>/signin-oidc</c> is a genuine cross-site top-level POST.
/// </para>
/// </summary>
public sealed class StubOidcProvider : IAsyncDisposable
{
    private readonly StubOidcSettings _settings;
    private readonly RsaSecurityKey _signingKey;
    private readonly ConcurrentDictionary<string, PendingCode> _codes = new();
    private readonly ConcurrentQueue<StubAuthorizeRequest> _authorizations = new();
    private WebApplication? _app;
    private int _authorizeSequence;
    private int _tokenRequests;

    public StubOidcProvider(StubOidcSettings settings)
    {
        _settings = settings;
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "browser-tests-key" };
    }

    public StubOidcBehavior Behavior { get; set; } = StubOidcBehavior.Succeed;

    public StubOidcIdentity? Identity { get; set; }

    public IReadOnlyList<StubAuthorizeRequest> Authorizations => _authorizations.ToArray();

    public int TokenRequestCount => Volatile.Read(ref _tokenRequests);

    public void Reset()
    {
        Behavior = StubOidcBehavior.Succeed;
        Identity = null;
        while (_authorizations.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _tokenRequests, 0);
        _codes.Clear();
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(_settings.Origin);
        var app = builder.Build();

        var tenantBase = $"/{_settings.TenantId}";
        app.MapGet($"{tenantBase}/v2.0/.well-known/openid-configuration", () => Results.Json(new Dictionary<string, object>
        {
            ["issuer"] = _settings.Authority,
            ["authorization_endpoint"] = $"{_settings.Origin}{tenantBase}/oauth2/v2.0/authorize",
            ["token_endpoint"] = $"{_settings.Origin}{tenantBase}/oauth2/v2.0/token",
            ["jwks_uri"] = $"{_settings.Origin}{tenantBase}/discovery/v2.0/keys",
            ["response_types_supported"] = new[] { "code", "id_token", "code id_token" },
            ["response_modes_supported"] = new[] { "query", "form_post" },
            ["subject_types_supported"] = new[] { "pairwise" },
            ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
            ["scopes_supported"] = new[] { "openid", "profile" },
        }));

        app.MapGet($"{tenantBase}/discovery/v2.0/keys", () =>
        {
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(_signingKey);
            return Results.Json(new
            {
                keys = new[]
                {
                    new { kty = jwk.Kty, use = "sig", kid = _signingKey.KeyId, alg = "RS256", n = jwk.N, e = jwk.E },
                },
            });
        });

        app.MapGet($"{tenantBase}/oauth2/v2.0/authorize", AuthorizeAsync);
        app.MapPost($"{tenantBase}/oauth2/v2.0/token", (Delegate)TokenAsync);

        await app.StartAsync().ConfigureAwait(false);
        _app = app;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is null)
            return;

        try
        {
            await _app.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch
        {
            // Teardown must never mask a test failure.
        }

        await _app.DisposeAsync().ConfigureAwait(false);
        _app = null;
    }

    private IResult AuthorizeAsync(HttpContext context)
    {
        var query = context.Request.Query;
        var redirectUri = query["redirect_uri"].ToString();
        var state = query["state"].ToString();
        var nonce = query["nonce"].ToString();
        var responseMode = query["response_mode"].ToString();
        if (string.IsNullOrEmpty(responseMode))
            responseMode = "query";

        var behavior = Behavior;
        _authorizations.Enqueue(new StubAuthorizeRequest(
            Interlocked.Increment(ref _authorizeSequence),
            responseMode,
            redirectUri,
            query.TryGetValue("prompt", out var prompt) ? prompt.ToString() : null,
            query.ContainsKey("claims"),
            !string.IsNullOrEmpty(state),
            behavior));

        var fields = new List<KeyValuePair<string, string>>();
        switch (behavior)
        {
            case StubOidcBehavior.AccessDenied:
                fields.Add(new("error", "access_denied"));
                fields.Add(new("error_description", "The user cancelled the sign-in."));
                break;
            case StubOidcBehavior.ProviderError:
                fields.Add(new("error", "server_error"));
                fields.Add(new("error_description", "The stub provider was told to fail."));
                break;
            default:
                var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                var wantsAcrs = query.ContainsKey("claims") && (Identity?.IncludeAuthenticationContext ?? false);
                _codes[code] = new PendingCode(nonce, Identity, wantsAcrs);
                fields.Add(new("code", code));
                break;
        }

        if (!string.IsNullOrEmpty(state))
            fields.Add(new("state", state));

        if (string.Equals(responseMode, "form_post", StringComparison.OrdinalIgnoreCase))
        {
            var html = new StringBuilder("<!doctype html><html><body onload=\"document.forms[0].submit()\">");
            html.Append("<form method=\"post\" action=\"").Append(WebUtility.HtmlEncode(redirectUri)).Append("\">");
            foreach (var (name, value) in fields)
            {
                html.Append("<input type=\"hidden\" name=\"").Append(WebUtility.HtmlEncode(name))
                    .Append("\" value=\"").Append(WebUtility.HtmlEncode(value)).Append("\"/>");
            }

            html.Append("<noscript><button>Continue</button></noscript></form></body></html>");
            context.Response.Headers.CacheControl = "no-store";
            return Results.Content(html.ToString(), "text/html");
        }

        var separator = redirectUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var target = redirectUri + separator + string.Join('&', fields.Select(f =>
            $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));
        return Results.Redirect(target);
    }

    private async Task<IResult> TokenAsync(HttpContext context)
    {
        Interlocked.Increment(ref _tokenRequests);
        var form = await context.Request.ReadFormAsync().ConfigureAwait(false);
        var code = form["code"].ToString();
        if (!_codes.TryRemove(code, out var pending) || pending.Identity is null)
            return Results.Json(new { error = "invalid_grant" }, statusCode: 400);

        var identity = pending.Identity;
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = identity.ObjectId.Replace("-", "", StringComparison.Ordinal),
            ["oid"] = identity.ObjectId,
            ["tid"] = identity.TenantId ?? _settings.TenantId,
            ["name"] = identity.DisplayName,
            ["preferred_username"] = "stub.user@example.invalid",
        };
        if (!string.IsNullOrEmpty(pending.Nonce))
            claims["nonce"] = pending.Nonce;
        if (pending.IncludeAuthenticationContext)
            claims["acrs"] = new[] { _settings.AuthenticationContextId };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _settings.Authority,
            Audience = _settings.ClientId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(30),
            Claims = claims,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        };

        var idToken = new JsonWebTokenHandler().CreateToken(descriptor);
        return Results.Json(new
        {
            token_type = "Bearer",
            scope = "openid profile",
            expires_in = 1800,
            access_token = "stub-access-token-not-a-credential",
            id_token = idToken,
        });
    }

    private sealed record PendingCode(string? Nonce, StubOidcIdentity? Identity, bool IncludeAuthenticationContext);
}
