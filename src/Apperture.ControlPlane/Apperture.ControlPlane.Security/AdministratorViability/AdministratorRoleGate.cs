using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>
/// The gate for a writer of role assignments, which does not know until it has read the role whether the Platform Administrator role
/// is the one it is changing (IAM-10.2b). The gate has to be held before the authority row locks, which are what read the role.
/// </summary>
public static class AdministratorRoleGate
{
    /// <summary>
    /// Takes the global gate when the request names the Platform Administrator role, or returns null when it names another role.
    /// The role is recognised by the id as typed and by the id of the stored row it resolves to (a catalog may match another
    /// spelling of the id), so that no spelling avoids the gate. It reads only the role row, and holds no lock while doing so.
    /// </summary>
    public static async ValueTask<AdministratorViabilityScope?> EnterForAdministratorRoleAsync(
        this IAdministratorViabilityCoordinator coordinator,
        IRoleRepository roles,
        RoleId requestedRoleId,
        CancellationToken cancellationToken)
    {
        var administratorRole = RoleAuthorityRules.IsPlatformAdministratorRole(new RoleId(requestedRoleId.Value.Trim()))
            || await roles.GetAsync(requestedRoleId, cancellationToken).ConfigureAwait(false) is { } stored
                && RoleAuthorityRules.IsPlatformAdministratorRole(stored.Id);
        return administratorRole ? await coordinator.EnterAsync(cancellationToken).ConfigureAwait(false) : null;
    }

    /// <summary>
    /// The decision above and the locked row must agree. The gate cannot be taken after an authority row lock without inverting the
    /// lock order, so a locked Platform Administrator role reached without the gate is a defect and the unit fails.
    /// </summary>
    public static void RequireGateHeldFor(RoleId lockedRoleId, AdministratorViabilityScope? scope)
    {
        if (scope is null && RoleAuthorityRules.IsPlatformAdministratorRole(lockedRoleId))
            throw new AuditInvariantException("The Platform Administrator role was reached without the administrator-set gate.");
    }
}
