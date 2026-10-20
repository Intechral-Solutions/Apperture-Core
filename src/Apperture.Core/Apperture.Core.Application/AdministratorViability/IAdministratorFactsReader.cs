namespace Apperture.Core.Application.AdministratorViability;

/// <summary>
/// What one holder of the Platform Administrator role has that decides whether they can sign in. Pure data: whether a sign-in
/// path is usable is decided from the effective authentication configuration, never here.
/// </summary>
/// <param name="UserId">The user.</param>
/// <param name="IsRecoveryAccess">The user is the designated Recovery Access account.</param>
/// <param name="HasLocalLink">The user has a Local identity link.</param>
/// <param name="LocalCredentialEnabled">The user has a Local credential that is not disabled.</param>
/// <param name="ProvenEntraLinkTenantIds">The directory (tenant) id of each Entra link that a successful sign-in has proven; null for a link stored without one.</param>
public sealed record AdministratorFacts(
    string UserId,
    bool IsRecoveryAccess,
    bool HasLocalLink,
    bool LocalCredentialEnabled,
    IReadOnlyList<string?> ProvenEntraLinkTenantIds);

/// <summary>The inputs of the Recovery Access posture, read as data.</summary>
/// <param name="DesignatedUserId">The designated user, or null when nobody is designated.</param>
/// <param name="HoldsAdministratorRole">The designated user holds the Platform Administrator role.</param>
/// <param name="HasLocalLink">The designated user has a Local identity link.</param>
/// <param name="LocalCredentialEnabled">The designated user has an enabled Local credential.</param>
/// <param name="NativeMfaEnrolled">The designated user has native MFA enrolled.</param>
/// <param name="UnusedRecoveryCodes">How many recovery codes of the designated user are unused.</param>
public sealed record RecoveryAccessFacts(
    string? DesignatedUserId,
    bool HoldsAdministratorRole,
    bool HasLocalLink,
    bool LocalCredentialEnabled,
    bool NativeMfaEnrolled,
    int UnusedRecoveryCodes)
{
    /// <summary>
    /// Demonstrably healthy, the existing posture exactly: a designated user who holds the administrator role, has an enabled Local
    /// credential, has MFA enrolled and has at least one unused recovery code. Nobody designated is not healthy.
    /// </summary>
    public bool IsHealthy =>
        DesignatedUserId is not null
        && HoldsAdministratorRole
        && HasLocalLink
        && LocalCredentialEnabled
        && NativeMfaEnrolled
        && UnusedRecoveryCodes > 0;
}

/// <summary>
/// Set-based reads of the administrator-viability inputs: a fixed number of statements, however many administrators exist. A caller
/// that needs a decision reads these after taking <see cref="IPlatformAdministratorSetGate"/>.
/// </summary>
public interface IAdministratorFactsReader
{
    /// <summary>Every holder of the Platform Administrator role, including the Recovery Access account (flagged).</summary>
    ValueTask<IReadOnlyList<AdministratorFacts>> ReadAdministratorsAsync(CancellationToken cancellationToken = default);

    /// <summary>The Recovery Access posture inputs.</summary>
    ValueTask<RecoveryAccessFacts> ReadRecoveryAccessAsync(CancellationToken cancellationToken = default);
}
