using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;

namespace Apperture.Management.Host;

/// <summary>
/// Carries an Entra strong-authentication step-up through the OIDC handler's protected
/// <see cref="AuthenticationProperties"/>. Ordinary login and self-link use different purpose values.
/// </summary>
internal static class EntraStepUpFlowConstants
{
    public const string PurposeValue = "strong-authentication-step-up";
    public const string RequestedContextItemKey = "apperture.auth.requestedAuthenticationContext";
    public const string ReturnPathItemKey = "apperture.auth.returnPath";
    public const string DefaultReturnPath = EntraLinkFlowConstants.AccountSecurityPath;

    public static bool IsStepUpPurpose(AuthenticationProperties? properties) =>
        properties?.Items.TryGetValue(EntraLinkFlowConstants.PurposeItemKey, out var purpose) == true
        && string.Equals(purpose, PurposeValue, StringComparison.Ordinal);

    public static string? GetRequestedContextId(AuthenticationProperties? properties) =>
        properties?.Items.TryGetValue(RequestedContextItemKey, out var value) == true && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    public static string GetReturnPath(AuthenticationProperties? properties)
    {
        if (properties?.Items.TryGetValue(ReturnPathItemKey, out var path) == true)
        {
            var normalized = ManagementUiRedirects.NormalizeRelativePath(path);
            if (normalized is not null && !EntraSignInChallenge.IsReservedAuthPath(normalized))
                return normalized;
        }

        return DefaultReturnPath;
    }

    public static OpenIdConnectChallengeProperties CreateChallengeProperties(
        IConfiguration configuration,
        string userId,
        string requestedContextId,
        string? returnUrl)
    {
        var path = ManagementUiRedirects.NormalizeRelativePath(returnUrl);
        if (path is null || EntraSignInChallenge.IsReservedAuthPath(path))
            path = DefaultReturnPath;

        var properties = new OpenIdConnectChallengeProperties
        {
            RedirectUri = ManagementUiRedirects.Build(configuration, path, "steppedUp", "1"),
            Prompt = "login"
        };
        properties.Items[EntraLinkFlowConstants.PurposeItemKey] = PurposeValue;
        properties.Items[EntraLinkFlowConstants.UserIdItemKey] = userId;
        properties.Items[RequestedContextItemKey] = requestedContextId;
        properties.Items[ReturnPathItemKey] = path;
        return properties;
    }
}
