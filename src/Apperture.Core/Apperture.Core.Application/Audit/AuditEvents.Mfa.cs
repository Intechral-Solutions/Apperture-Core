using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Audit;

// AUD-1.2: native MFA. The target is the user whose authenticator or recovery codes changed. A code, secret or hash is
// never recorded; only counts.
public static partial class AuditEvents
{
    /// <summary>An authenticator was enrolled for the user.</summary>
    public static AuditEvent MfaEnrolled(UserId user) =>
        Event(
            AuditActionNames.MfaEnrolled,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value));

    /// <summary>A new set of one-time recovery codes replaced the user's previous ones.</summary>
    public static AuditEvent RecoveryCodesGenerated(UserId user, int count, bool regenerated) =>
        Event(
            AuditActionNames.RecoveryCodesGenerated,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value),
            ("count", count),
            ("regenerated", regenerated));

    /// <summary>One of the user's recovery codes was used to complete a sign-in.</summary>
    public static AuditEvent RecoveryCodeConsumed(UserId user, int remaining) =>
        Event(
            AuditActionNames.RecoveryCodeConsumed,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value),
            ("remaining", remaining));

    /// <summary>The user's authenticator and recovery codes were removed.</summary>
    /// <param name="configurationEvidence">The class of configuration evidence the decision used, when the operation records it.</param>
    public static AuditEvent MfaReset(UserId user, ConfigurationEvidence? configurationEvidence = null) =>
        Event(
            AuditActionNames.MfaReset,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value),
            (ConfigurationEvidenceNames.AuditDetailName, configurationEvidence?.ToAuditValue()));

    /// <summary>
    /// The user's authenticator was replaced by a candidate the operator proved possession of, and the recovery codes were replaced
    /// with it. Only the user and the class of evidence are recorded: never a secret, a code, a hash or a candidate.
    /// </summary>
    /// <param name="configurationEvidence">The class of configuration evidence the decision used, when the operation records it.</param>
    public static AuditEvent MfaReplaced(UserId user, ConfigurationEvidence? configurationEvidence = null) =>
        Event(
            AuditActionNames.MfaReplaced,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value),
            (ConfigurationEvidenceNames.AuditDetailName, configurationEvidence?.ToAuditValue()));
}
