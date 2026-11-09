using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>
/// The one mapping from a decision about administrator viability to the refusal row of a guarded Recovery Access or authenticator
/// operation (<c>emergency.auth.blocked.administrator_viability</c>): the operation the decision's mutation names, the stable code the
/// operator was given, and the class of configuration evidence the decision was made under. Every service that records such a refusal
/// builds its row here, so the operation vocabulary, the code and the evidence cannot drift apart between writers.
/// </summary>
public static class ViabilityRefusalAudit
{
    /// <summary>
    /// The operation a refusal of <paramref name="mutation"/> records. Only the four guarded operations of the command line have one; any
    /// other change has a refusal event of its own and is rejected here, so it can never be recorded under a borrowed name.
    /// </summary>
    public static string OperationOf(ViabilityMutation mutation) => mutation switch
    {
        ViabilityMutation.RecoveryAccessDesignation => "recovery_access.designate",
        ViabilityMutation.RecoveryAccessRevocation => "recovery_access.revoke",
        ViabilityMutation.AuthenticatorReset => "mfa.reset",
        ViabilityMutation.AuthenticatorReplacement => "mfa.replace",
        _ => throw new ArgumentOutOfRangeException(
            nameof(mutation),
            mutation,
            "This change is not a guarded Recovery Access or authenticator operation; its refusal is recorded by an event of its own."),
    };

    /// <summary>
    /// The evidence class a refusal row records: the decision's own class whenever it has one. A decision without one was made by a host
    /// on its own running options, which attests nothing and needed no evidence about another process; the refusal's schema requires a
    /// class, and of the three only <see cref="ConfigurationEvidence.NotRequired"/> is true of it (no attestation was read, so it was not
    /// attested, and nothing it needed was missing, so it was not unavailable). The success row of such a decision leaves the detail out
    /// instead, as the producers' optional parameter and <see cref="ViabilityDecision.Evidence"/> state.
    /// </summary>
    public static ConfigurationEvidence RefusalEvidence(ConfigurationEvidence? evidence) =>
        evidence ?? ConfigurationEvidence.NotRequired;

    /// <summary>The refusal row of a decision that refuses, with the code of its own typed refusal.</summary>
    /// <param name="user">The affected account, named by the id of the row the operation loaded.</param>
    /// <param name="decision">A decision that does not allow the change.</param>
    public static AuditEvent Blocked(UserId user, ViabilityDecision decision) =>
        Blocked(user, decision, decision.Refusal().Code);

    /// <summary>
    /// The refusal row of <paramref name="decision"/>'s operation with an explicit <paramref name="code"/>: for an operation that refuses
    /// on a rule of its own about the decision (a replacement whose decision was not made on the posture alone), whatever the verdict.
    /// </summary>
    public static AuditEvent Blocked(UserId user, ViabilityDecision decision, string code)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return AuditEvents.AdministratorViabilityBlocked(user, OperationOf(decision.Mutation), code, RefusalEvidence(decision.Evidence));
    }
}
