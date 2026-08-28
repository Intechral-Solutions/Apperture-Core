namespace Apperture.ControlPlane.Security;

/// <summary>
/// Configuration for Microsoft Entra (Azure AD) OpenID Connect authentication.
/// Bind from section "Entra" or "Authentication:Entra".
/// </summary>
public sealed class EntraAuthOptions
{
    public const string SectionName = "Entra";

    /// <summary>
    /// When true, Entra authentication is enabled and must be configured.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Entra tenant authority (e.g. https://login.microsoftonline.com/{tenantId}).
    /// </summary>
    public string Authority { get; set; } = "";

    /// <summary>
    /// Application (client) ID.
    /// </summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// Client secret for confidential client. Required when using authorization code flow.
    /// </summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Optional Azure AD directory id. Used to build a tenant-specific OIDC authority when
    /// <see cref="Authority"/> is <c>common</c>/<c>organizations</c> or omits a tenant segment.
    /// </summary>
    public string? AzureTenantId { get; set; }

    /// <summary>
    /// Callback path after Entra redirect (e.g. /signin-oidc).
    /// </summary>
    public string CallbackPath { get; set; } = "/signin-oidc";

    /// <summary>
    /// Scopes to request (e.g. openid, profile).
    /// </summary>
    public string[] Scopes { get; set; } = ["openid", "profile"];

    /// <summary>
    /// Optional Microsoft Entra Conditional Access authentication-context identifier (<c>c1</c>–<c>c25</c>)
    /// requested during strong-authentication step-up. Empty means step-up is not configured.
    /// Satisfying this identifier is what Apperture trusts; it is not interpreted as a specific MFA method.
    /// </summary>
    public string? AuthenticationContextId { get; set; }

    /// <summary>
    /// Development-only: treat a completed Entra step-up as satisfying the configured authentication
    /// context even when the id_token does not contain <c>acrs</c>. Ignored unless the host environment
    /// is Development. Never enable this in production.
    /// </summary>
    public bool DevelopmentSimulateAuthenticationContext { get; set; }
}
