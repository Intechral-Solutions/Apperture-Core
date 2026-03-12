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
    /// Callback path after Entra redirect (e.g. /signin-oidc).
    /// </summary>
    public string CallbackPath { get; set; } = "/signin-oidc";

    /// <summary>
    /// Scopes to request (e.g. openid, profile).
    /// </summary>
    public string[] Scopes { get; set; } = ["openid", "profile"];
}
