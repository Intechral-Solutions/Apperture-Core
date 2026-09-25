using Apperture.Management.Ui.Shared.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;

namespace Apperture.Management.Host;

/// <summary>
/// Carries the purpose of an OIDC round trip through the handler's own protected
/// <see cref="AuthenticationProperties"/>. There is no second signed-state format: correlation and
/// state protection remain the OIDC middleware's responsibility.
/// </summary>
internal static class EntraLinkFlowConstants
{
    public const string PurposeItemKey = "apperture.auth.purpose";
    public const string UserIdItemKey = "apperture.auth.userId";
    public const string LinkPurposeValue = "link-entra";

    /// <summary>Where the UI returns after a self-service link or step-up attempt.</summary>
    public const string AccountSecurityPath = "/account";

    public const string LoginPath = "/login";

    public const string AccessRequestPath = "/access-request";

    public static bool IsLinkPurpose(AuthenticationProperties? properties) =>
        properties?.Items.TryGetValue(PurposeItemKey, out var purpose) == true
        && string.Equals(purpose, LinkPurposeValue, StringComparison.Ordinal);

    public static string? GetInitiatingUserId(AuthenticationProperties? properties) =>
        properties?.Items.TryGetValue(UserIdItemKey, out var userId) == true && !string.IsNullOrWhiteSpace(userId)
            ? userId
            : null;

    public static OpenIdConnectChallengeProperties CreateChallengeProperties(
        IConfiguration configuration,
        string userId,
        string? returnUrl)
    {
        var safeReturnPath = LocalReturnUrl.Validate(returnUrl) ?? AccountSecurityPath;
        var properties = new OpenIdConnectChallengeProperties
        {
            RedirectUri = ManagementUiRedirects.Build(configuration, safeReturnPath, "linked", "1"),
            // Force an account prompt so the operator consciously picks which Microsoft account gets linked.
            Prompt = "login"
        };
        properties.Items[PurposeItemKey] = LinkPurposeValue;
        properties.Items[UserIdItemKey] = userId;
        return properties;
    }
}
