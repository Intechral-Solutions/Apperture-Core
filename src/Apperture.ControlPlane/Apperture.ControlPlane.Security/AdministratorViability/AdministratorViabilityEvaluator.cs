using Apperture.Core.Application.AdministratorViability;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>
/// The one definition of a viable ordinary administrator (VOA) and an Entra-independent administrator (EI), as pure functions of
/// the stored facts and the sign-in availability of one view (IAM-10 design gate §3.2).
/// </summary>
public static class AdministratorViabilityEvaluator
{
    /// <summary>
    /// <b>VOA</b>: holds the Platform Administrator role, is <i>not</i> Recovery Access, and has a usable sign-in method. A method
    /// is usable when it is an enabled Local credential while Local sign-in is enabled, or a <i>proven</i> Entra link whose tenant
    /// is the tenant the authentication accepts (<see cref="EntraTokenClaims.TenantMatches"/>, the sign-in's own comparison) while
    /// Entra sign-in is usable. <b>EI</b>: a VOA whose Local method is usable. Recovery Access is never counted in either.
    /// </summary>
    public static AdministratorViabilitySnapshot Evaluate(
        IReadOnlyList<AdministratorFacts> administrators,
        SignInAvailability availability)
    {
        var voa = new HashSet<string>(StringComparer.Ordinal);
        var ei = new HashSet<string>(StringComparer.Ordinal);

        foreach (var admin in administrators)
        {
            if (admin.IsRecoveryAccess)
                continue;

            var localUsable = availability.LocalSignInEnabled && admin.HasLocalLink && admin.LocalCredentialEnabled;
            var entraUsable = availability.EntraSignInUsable
                && availability.AcceptedEntraTenantId is not null
                && admin.ProvenEntraLinkTenantIds.Any(tenant => EntraTokenClaims.TenantMatches(tenant, availability.AcceptedEntraTenantId));

            if (localUsable || entraUsable)
                voa.Add(admin.UserId);
            if (localUsable)
                ei.Add(admin.UserId);
        }

        return new AdministratorViabilitySnapshot(availability.View, voa, ei);
    }
}
