using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Canonical system-managed Emergency Access principal. Username and display name are platform
/// invariants, not administrator-configurable. Designation remains <see cref="User.IsBreakGlassEligible"/>.
/// </summary>
public static class EmergencyAccessIdentity
{
    /// <summary>Reserved local username. Normalized form is identical (trim + lowercase).</summary>
    public const string LocalUsername = UsernamePolicy.RecoveryAccessUsername;

    /// <summary>Operator-facing display name for the recovery principal.</summary>
    public const string DisplayName = "Recovery Access";

    /// <summary>Capability name (the recovery path), distinct from <see cref="DisplayName"/> (the principal).</summary>
    public const string CapabilityName = "Emergency Access";

    public static string NormalizedLocalUsername => LocalUsername;

    /// <summary>
    /// True for the Recovery Access username only. The wider reserved list for new assignments is
    /// <see cref="UsernamePolicy.ReservedNames"/>.
    /// </summary>
    public static bool IsReservedLocalUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return false;
        return string.Equals(UsernamePolicy.Normalize(username), LocalUsername, StringComparison.Ordinal);
    }

    /// <summary>The designated emergency user is system-managed; ordinary user-administration must not mutate it.</summary>
    public static bool IsSystemManaged(User user) => user.IsBreakGlassEligible;

    public static string? LocalUsernameOf(User user) =>
        user.IdentityLinks.FirstOrDefault(l => l.Source == IdentitySource.Local)?.ExternalId;

    public static bool HasCanonicalIdentity(User user)
    {
        var local = LocalUsernameOf(user);
        return string.Equals(user.DisplayName, DisplayName, StringComparison.Ordinal)
            && string.Equals(user.Username, LocalUsername, StringComparison.Ordinal)
            && string.Equals(local, LocalUsername, StringComparison.Ordinal);
    }

    public static bool HasEntraIdentityLink(User user) =>
        user.IdentityLinks.Any(l => l.Source == IdentitySource.Entra);
}
