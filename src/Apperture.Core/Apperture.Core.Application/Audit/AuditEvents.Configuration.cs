namespace Apperture.Core.Application.Audit;

// AUD-1.2: the typed producers of Entra tenant registrations, the access-request policy and the sign-in policy. The client
// secret is never recorded; only whether it was replaced.
public static partial class AuditEvents
{
    /// <summary>The id of the instance's single sign-in policy, its persisted row (<c>InstanceAuthUiPolicies.Id</c>).</summary>
    public const string SignInPolicyId = "instance";

    /// <summary>An Entra tenant registration was added.</summary>
    public static AuditEvent TenantRegistrationCreated(
        string registrationId,
        string displayName,
        string clientId,
        string? azureTenantId,
        bool enabled,
        bool clientAuthenticationReplaced,
        bool allowMemberAccessRequests,
        bool allowGuestAccessRequests) =>
        Event(
            AuditActionNames.TenantRegistrationCreated,
            Target(AuditTargetTypes.EntraTenantRegistration, registrationId),
            AuditOutcomeCodes.Succeeded,
            ("registrationId", registrationId),
            ("displayName", displayName),
            ("clientId", clientId),
            ("azureTenantId", azureTenantId),
            ("enabled", enabled),
            ("clientAuthenticationReplaced", clientAuthenticationReplaced),
            ("allowMemberAccessRequests", allowMemberAccessRequests),
            ("allowGuestAccessRequests", allowGuestAccessRequests));

    /// <summary>
    /// An Entra tenant registration was changed. <paramref name="changed"/> names the settings that differ (never their
    /// values); it is never empty, since an update that changes nothing is not recorded.
    /// </summary>
    public static AuditEvent TenantRegistrationUpdated(string registrationId, string displayName, IReadOnlyList<string> changed, bool enabled, bool active)
    {
        if (changed.Count == 0)
            throw new ArgumentException("An update that changes nothing is not audited as a change.", nameof(changed));
        if (changed.FirstOrDefault(c => !AuditDetailSchemas.TenantRegistrationSettings.Contains(c, StringComparer.Ordinal)) is { } unknown)
            throw new ArgumentException($"'{unknown}' is not a tenant registration setting.", nameof(changed));
        return Event(
            AuditActionNames.TenantRegistrationUpdated,
            Target(AuditTargetTypes.EntraTenantRegistration, registrationId),
            AuditOutcomeCodes.Succeeded,
            ("registrationId", registrationId),
            ("displayName", displayName),
            ("changed", changed),
            ("enabled", enabled),
            ("active", active));
    }

    /// <summary>An Entra tenant registration was deleted. The name is a snapshot: the registration no longer exists.</summary>
    public static AuditEvent TenantRegistrationDeleted(string registrationId, string displayName, bool wasActive) =>
        Event(AuditActionNames.TenantRegistrationDeleted, Target(AuditTargetTypes.EntraTenantRegistration, registrationId), AuditOutcomeCodes.Succeeded,
            ("registrationId", registrationId), ("displayName", displayName), ("wasActive", wasActive));

    /// <summary>An Entra tenant registration became the one Microsoft sign-in uses.</summary>
    public static AuditEvent TenantRegistrationActivated(string registrationId, string? previousRegistrationId) =>
        Event(AuditActionNames.TenantRegistrationActivated, Target(AuditTargetTypes.EntraTenantRegistration, registrationId), AuditOutcomeCodes.Succeeded,
            ("registrationId", registrationId), ("previousRegistrationId", previousRegistrationId));

    /// <summary>
    /// A registration change was refused by a state guard read in its transaction, and nothing changed.
    /// <paramref name="change"/> is <c>update</c>, <c>activate</c> or <c>delete</c>; <paramref name="reason"/> is
    /// <c>sign_in_lockout</c> (the change would leave no viable sign-in path) or <c>registration_disabled</c>.
    /// </summary>
    public static AuditEvent TenantRegistrationChangeRefused(string registrationId, string change, string reason) =>
        Event(AuditActionNames.TenantRegistrationChangeRefused, Target(AuditTargetTypes.EntraTenantRegistration, registrationId), AuditOutcomeCodes.Rejected,
            ("registrationId", registrationId), ("change", change), ("reason", reason));

    /// <summary>Whether members and guests of a registration's directory may request access changed.</summary>
    public static AuditEvent AccessRequestPolicyChanged(string registrationId, bool allowMemberAccessRequests, bool allowGuestAccessRequests) =>
        Event(AuditActionNames.AccessRequestPolicyChanged, Target(AuditTargetTypes.EntraTenantRegistration, registrationId), AuditOutcomeCodes.Succeeded,
            ("registrationId", registrationId), ("allowMemberAccessRequests", allowMemberAccessRequests), ("allowGuestAccessRequests", allowGuestAccessRequests));

    /// <summary>The instance sign-in policy changed; <paramref name="changed"/> names the settings that differ.</summary>
    public static AuditEvent SignInPolicyUpdated(
        IReadOnlyList<string> changed,
        bool showLocalSignInInPrimaryChooser,
        bool preferEntraInChooser,
        bool autoRedirectToEntra)
    {
        if (changed.Count == 0)
            throw new ArgumentException("An update that changes nothing is not audited as a change.", nameof(changed));
        return Event(
            AuditActionNames.SignInPolicyUpdated,
            Target(AuditTargetTypes.AuthPolicy, SignInPolicyId),
            AuditOutcomeCodes.Succeeded,
            ("changed", changed),
            ("showLocalSignInInPrimaryChooser", showLocalSignInInPrimaryChooser),
            ("preferEntraInChooser", preferEntraInChooser),
            ("autoRedirectToEntra", autoRedirectToEntra));
    }
}
