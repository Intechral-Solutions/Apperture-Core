using System.Security.Claims;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Builds read-only account fields for <c>GET /api/auth/me</c> from domain data and the current principal.
/// </summary>
public static class AccountMeSnapshotBuilder
{
    public const string LocalCredentialStatusNone = "none";
    public const string LocalCredentialStatusActive = "active";
    public const string LocalCredentialStatusDisabled = "disabled";

    public static string ResolveSignInSource(ClaimsPrincipal principal)
    {
        var claim = principal.FindFirst(ClaimsPrincipalToActorMapper.IdentitySourceClaimType)?.Value;
        if (string.Equals(claim, "Local", StringComparison.OrdinalIgnoreCase))
            return "Local";
        if (string.Equals(claim, "Entra", StringComparison.OrdinalIgnoreCase))
            return "Entra";
        return string.IsNullOrWhiteSpace(claim) ? "Unknown" : claim;
    }

    public static string ResolveLocalCredentialStatus(User user, LocalCredentialData? credential)
    {
        if (!user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
            return LocalCredentialStatusNone;

        if (credential is null)
            return LocalCredentialStatusNone;

        return credential.IsDisabled ? LocalCredentialStatusDisabled : LocalCredentialStatusActive;
    }

    public static bool CanChangeLocalPassword(bool localAuthEnabled, string localCredentialStatus) =>
        localAuthEnabled && localCredentialStatus == LocalCredentialStatusActive;

    public static string FormatIdentityLinkLabel(IdentitySource source, string externalId)
    {
        if (source == IdentitySource.Entra)
        {
            var suffix = externalId.Length >= 4 ? externalId[^4..] : externalId;
            return $"Entra · …{suffix}";
        }

        return externalId;
    }

    public static AccountDeferredFeaturesSnapshot BuildDeferredFeatures() =>
        new(
            AvatarManagementDeferred: true,
            ProfileEditDeferred: true,
            NotificationSubscriptionsDeferred: false,
            Message: "Avatar and profile editing are not available in this release. Manage email notification preferences from My notifications.");
}

public sealed record AccountDeferredFeaturesSnapshot(
    bool AvatarManagementDeferred,
    bool ProfileEditDeferred,
    bool NotificationSubscriptionsDeferred,
    string Message);
