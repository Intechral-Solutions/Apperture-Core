using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Entra-primary authentication policy: after an Entra identity is proven, local password
/// sign-in is disabled unless doing so would remove the last Entra-independent Platform
/// Administrator recovery path (temporary until IAM-3).
/// </summary>
public interface IEntraPrimaryAuthenticationPolicy
{
    /// <summary>
    /// Applies the policy after successful Entra authentication of a proven identity. Call this on
    /// every successful Entra sign-in, not only the first proof: a previously deferred last-admin
    /// disable is re-evaluated when another Entra-independent Platform Administrator exists.
    /// Idempotent for already-disabled credentials: they are left unchanged and are not re-audited.
    /// </summary>
    ValueTask<EntraPrimaryLocalDisableOutcome> ApplyAfterEntraProvenAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Presentation of Entra association and local sign-in policy for Account Security and user administration.
    /// </summary>
    ValueTask<LocalAuthenticationPresentation> DescribeAsync(
        User user,
        LocalCredentialData? credential,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of attempting to disable local authentication after Entra proof.</summary>
public enum EntraPrimaryLocalDisableOutcome
{
    NoLocalCredential,
    AlreadyDisabled,
    Disabled,
    DeferredLastEntraIndependentPlatformAdministrator
}

/// <summary>
/// Operator-facing authentication-method state. Values are stable API strings.
/// </summary>
public sealed record LocalAuthenticationPresentation(
    string EntraAssociationStatus,
    string LocalSignInPolicy,
    bool RecoverySafeguardActive);

/// <summary>Stable status strings for Entra association and local sign-in policy.</summary>
public static class AuthenticationMethodStatuses
{
    public const string EntraNone = "none";
    public const string EntraPending = "pending";
    public const string EntraProven = "proven";

    public const string LocalNone = "none";
    public const string LocalEnabled = "enabled";
    public const string LocalDisabled = "disabled";
    public const string LocalRecoverySafeguard = "recovery_safeguard";
}
