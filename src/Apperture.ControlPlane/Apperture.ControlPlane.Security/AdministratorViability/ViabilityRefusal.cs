namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>Which mutation was refused, so that the one violated condition is given the code that names the thing to fix.</summary>
public enum ViabilityMutation
{
    /// <summary>The Platform Administrator role is removed from a user.</summary>
    AdministratorRoleRemoval,

    /// <summary>A Local credential is disabled.</summary>
    LocalCredentialDisable,

    /// <summary>A Local or Entra identity link is removed.</summary>
    IdentityLinkRemoval,

    /// <summary>The Microsoft registration is edited, disabled, deleted, or another registration is made the active one.</summary>
    RegistrationChange,
}

/// <summary>
/// The typed refusal of a viability verdict (IAM-10 design gate P-11): a <see cref="GovernanceConflictException"/> (409) whose stable
/// code and message name the condition that was violated and what to do about it. A verdict that allows the change has no refusal.
/// </summary>
public static class ViabilityRefusal
{
    /// <summary>The refusal for <paramref name="verdict"/>. Throws when the verdict allows the change.</summary>
    public static GovernanceConflictException For(ViabilityVerdict verdict, ViabilityMutation mutation) =>
        verdict.Kind switch
        {
            ViabilityVerdictKind.LastViableAdministrator => mutation switch
            {
                ViabilityMutation.AdministratorRoleRemoval => new(
                    GovernanceConflictCodes.LastViableAdministrator,
                    "Cannot remove the Platform Administrator role: no other ordinary Platform Administrator would be able to sign in. Give another administrator a working sign-in method first."),
                ViabilityMutation.LocalCredentialDisable => new(
                    GovernanceConflictCodes.LastLocalCredential,
                    "Cannot disable this local credential: it is the only sign-in method the last ordinary Platform Administrator who can sign in has. Give another administrator, or this one, another working sign-in method first."),
                ViabilityMutation.RegistrationChange => new(
                    GovernanceConflictCodes.LastViableSignInMethod,
                    "Cannot make this change to the Microsoft registration: it would leave no ordinary Platform Administrator able to sign in, because the administrators who can sign in today depend on Microsoft sign-in as this registration provides it. Give an administrator a working local sign-in first, or keep the registration as it is."),
                _ => new(
                    GovernanceConflictCodes.LastViableSignInMethod,
                    "Cannot remove this sign-in method: it is the only one the last ordinary Platform Administrator who can sign in has. A Microsoft identity counts only after it has been used to sign in, and only while Microsoft sign-in is working for this installation."),
            },
            ViabilityVerdictKind.EntraIndependentFallbackRequired => new(
                GovernanceConflictCodes.EntraIndependentFallbackRequired,
                "Recovery Access is not healthy, and this change would leave no ordinary Platform Administrator who can sign in without Microsoft. Give an administrator an enabled local credential, or restore Recovery Access, then try again."),
            ViabilityVerdictKind.ConfigurationUnavailable => new(
                GovernanceConflictCodes.AdministratorViabilityUnavailable,
                "The change could not be checked because the sign-in configuration could not be established, so it was not made. " + verdict.Detail),
            _ => throw new ArgumentException("An allowed verdict has no refusal.", nameof(verdict)),
        };
}
