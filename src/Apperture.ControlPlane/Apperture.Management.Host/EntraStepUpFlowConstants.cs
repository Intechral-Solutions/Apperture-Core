using Apperture.Management.Ui.Shared.Auth;
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

    /// <summary>
    /// The kind of session that started the step-up (<see cref="SessionSourceLocal"/> or <see cref="SessionSourceEntra"/>), so the
    /// callback can re-issue the same kind of session without reading the ordinary session cookie, which a cross-site
    /// <c>form_post</c> does not carry (SEC-1 finding F8). Recorded at initiation from the live principal; it names a kind of
    /// session, never an authorization decision, and every permission is still evaluated per request.
    /// </summary>
    public const string SessionSourceItemKey = "apperture.auth.sessionSource";
    public const string SessionSourceLocal = "Local";
    public const string SessionSourceEntra = "Entra";

    /// <summary>The continuation keys a step-up puts in the OIDC state, all of which the callback requires (nothing else is trusted).</summary>
    public static readonly string[] ContinuationItemKeys =
    [
        EntraLinkFlowConstants.UserIdItemKey,
        RequestedContextItemKey,
        ReturnPathItemKey,
        SessionSourceItemKey,
    ];

    /// <summary>
    /// How long a step-up may take between the challenge and its callback. The OpenID Connect handler does NOT enforce the
    /// protected state's own expiry (it relies on the browser dropping the correlation cookie), so the step-up records an expiry
    /// in the state (<see cref="AuthenticationProperties.ExpiresUtc"/>, protected with it) and the callback enforces it.
    /// Matches the handler's own remote-authentication timeout (15 minutes).
    /// </summary>
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    /// <summary>True when the state carries an expiry that has not passed. A state with no expiry is not a step-up this build issued.</summary>
    public static bool IsFresh(AuthenticationProperties? properties, DateTimeOffset utcNow) =>
        properties?.ExpiresUtc is { } expires && expires > utcNow;

    /// <summary>The session kind recorded at initiation, or null when absent or not a known kind.</summary>
    public static string? GetSessionSource(AuthenticationProperties? properties) =>
        properties?.Items.TryGetValue(SessionSourceItemKey, out var value) == true
            ? value switch
            {
                SessionSourceLocal => SessionSourceLocal,
                SessionSourceEntra => SessionSourceEntra,
                _ => null,
            }
            : null;

    /// <summary>
    /// Removes the step-up continuation from properties that are about to become the session cookie's properties, so the
    /// continuation never outlives the OIDC state it was carried in.
    /// </summary>
    public static void RemoveContinuation(AuthenticationProperties properties)
    {
        // The state's expiry (minutes) must never become the session cookie's expiry.
        properties.ExpiresUtc = null;
        properties.IssuedUtc = null;
        properties.Items.Remove(EntraLinkFlowConstants.PurposeItemKey);
        foreach (var key in ContinuationItemKeys)
            properties.Items.Remove(key);
    }

    public static bool IsStepUpPurpose(AuthenticationProperties? properties) =>
        properties?.Items.TryGetValue(EntraLinkFlowConstants.PurposeItemKey, out var purpose) == true
        && string.Equals(purpose, PurposeValue, StringComparison.Ordinal);

    public static string? GetRequestedContextId(AuthenticationProperties? properties) =>
        properties?.Items.TryGetValue(RequestedContextItemKey, out var value) == true && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    public static string GetReturnPath(AuthenticationProperties? properties)
    {
        if (properties?.Items.TryGetValue(ReturnPathItemKey, out var path) == true
            && LocalReturnUrl.Validate(path) is { } valid)
        {
            return valid;
        }

        return DefaultReturnPath;
    }

    public static OpenIdConnectChallengeProperties CreateChallengeProperties(
        IConfiguration configuration,
        string userId,
        string requestedContextId,
        string? returnUrl,
        string sessionSource)
    {
        var path = LocalReturnUrl.Validate(returnUrl) ?? DefaultReturnPath;

        var properties = new OpenIdConnectChallengeProperties
        {
            RedirectUri = ManagementUiRedirects.Build(configuration, path, "steppedUp", "1"),
            Prompt = "login"
        };
        properties.Items[EntraLinkFlowConstants.PurposeItemKey] = PurposeValue;
        properties.Items[EntraLinkFlowConstants.UserIdItemKey] = userId;
        properties.Items[RequestedContextItemKey] = requestedContextId;
        properties.Items[ReturnPathItemKey] = path;
        properties.Items[SessionSourceItemKey] = sessionSource;
        properties.ExpiresUtc = DateTimeOffset.UtcNow.Add(StateLifetime);
        return properties;
    }
}
