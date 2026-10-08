namespace Apperture.Core.Application.Audit;

// AUD-1.4: the typed producers of secret-store events (Secrets category). Per the gate's §22 ruling on secret audit
// identifiers, each event targets the configuration entity that owns the secret and is staged by that entity's change, in
// its unit, as a secondary row beside the change's own primary row. The store itself records nothing here: it does not know
// the owner, and SecretAudits stays its separate evidence. No producer takes a secret, a secret reference or material.
public static partial class AuditEvents
{
    /// <summary>
    /// A new client secret was stored for an Entra tenant registration and became the one it names, in the same transaction as
    /// the registration change <paramref name="change"/> (<c>registration_created</c> or <c>registration_updated</c>).
    /// </summary>
    public static AuditEvent TenantRegistrationClientSecretStored(string registrationId, string change) =>
        Event(AuditActionNames.TenantRegistrationClientSecretStored, Target(AuditTargetTypes.EntraTenantRegistration, registrationId), AuditOutcomeCodes.Succeeded,
            ("registrationId", registrationId), ("change", change));

    /// <summary>
    /// A client secret the registration owned was retired (disabled in the secret store), in the same transaction as the change
    /// that stopped naming it: <c>replaced</c> by a new secret, or <c>registration_deleted</c>.
    /// </summary>
    public static AuditEvent TenantRegistrationClientSecretRetired(string registrationId, string reason) =>
        Event(AuditActionNames.TenantRegistrationClientSecretRetired, Target(AuditTargetTypes.EntraTenantRegistration, registrationId), AuditOutcomeCodes.Succeeded,
            ("registrationId", registrationId), ("reason", reason));
}
