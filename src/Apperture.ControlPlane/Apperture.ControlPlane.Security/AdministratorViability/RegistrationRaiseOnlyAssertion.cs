using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>
/// What a raise-only registration writer asserts (IAM-10.2b WP3). Adding a registration can only add: a new registration is never
/// the active one, so it cannot change the Microsoft sign-in the host accepts. The assertion is attributed to the writer's own
/// effect, the persisted <i>active</i> registration before and after, and not to a comparison of the whole system, so a
/// configuration reload, a Recovery Access recovery code consumed at sign-in or another administrator's change between its two
/// reads (the lesson of the WP2 review, M-2) cannot fail it. Every writer of the active registration takes the administrator-set
/// gate, so while this writer holds it nothing else can have changed it.
/// </summary>
public static class RegistrationRaiseOnlyAssertion
{
    /// <summary>
    /// Throws <see cref="AuditInvariantException"/> (a defect of the writer: the unit rolls back and the failure is an internal
    /// error, never a business outcome) when the registration <paramref name="createdId"/> is active after the write, or when the
    /// active registration, or any setting of it the sign-in reads, differs between <paramref name="before"/> and <paramref name="after"/>.
    /// </summary>
    public static void RequireActiveRegistrationUnchanged(EntraInstanceRegistration? before, EntraInstanceRegistration? after, string createdId)
    {
        if (after is not null && string.Equals(after.Id, createdId, StringComparison.Ordinal))
            throw new AuditInvariantException("A change that may only add a registration made the new registration the active one.");

        if (!SameSignInSettings(before, after))
            throw new AuditInvariantException("A change that may only add a registration changed the active registration.");
    }

    /// <summary>True when both are the same registration with the same settings as far as the sign-in is concerned, or both are none.</summary>
    internal static bool SameSignInSettings(EntraInstanceRegistration? a, EntraInstanceRegistration? b)
    {
        if (a is null || b is null)
            return a is null && b is null;

        return string.Equals(a.Id, b.Id, StringComparison.Ordinal)
               && a.Enabled == b.Enabled
               && a.IsActive == b.IsActive
               && string.Equals(a.Authority, b.Authority, StringComparison.Ordinal)
               && string.Equals(a.ClientId, b.ClientId, StringComparison.Ordinal)
               && string.Equals(a.AzureTenantId, b.AzureTenantId, StringComparison.Ordinal)
               && string.Equals(a.AuthenticationContextId, b.AuthenticationContextId, StringComparison.Ordinal)
               && string.Equals(a.CallbackPath, b.CallbackPath, StringComparison.Ordinal)
               && a.UsesDeploymentSecret == b.UsesDeploymentSecret
               && string.Equals(a.ClientSecretRef, b.ClientSecretRef, StringComparison.Ordinal);
    }
}
