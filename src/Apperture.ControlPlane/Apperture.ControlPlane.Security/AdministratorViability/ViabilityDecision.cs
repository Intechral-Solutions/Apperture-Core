using Apperture.Core.Application.AdministratorViability;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>What settled a <see cref="ViabilityDecision"/>: which inputs it actually depended on.</summary>
public enum ViabilityDecisionBasis
{
    /// <summary>Nothing a viability definition reads differs between the two states, so the configuration was not needed.</summary>
    InputsUnchanged,

    /// <summary>The change touched only an authenticator, and by the Recovery Access posture alone it cannot have reduced anything.</summary>
    PostureOnly,

    /// <summary>The decision needed Local sign-in, per view, and no Microsoft reading: the stored Local facts under each view's Local setting.</summary>
    LocalSignIn,

    /// <summary>The two invariants were evaluated over both views, which needed both the Local and the Microsoft readings.</summary>
    FullEvaluation,

    /// <summary>The decision needed a reading that could not be relied on, so it failed closed.</summary>
    ConfigurationUnavailable,
}

/// <summary>
/// The outcome of a verification of an evidence-dependent or net-judged change: the verdict, the class of evidence it actually used,
/// and what settled it. Everything a caller records and refuses with, in one value, so that a refusal cannot be given the wrong
/// operation's wording and an audit row cannot claim evidence the decision did not use.
/// </summary>
/// <param name="Mutation">The change that was judged.</param>
/// <param name="Verdict">Whether it may stand.</param>
/// <param name="Evidence">
/// The class to record as <see cref="ConfigurationEvidenceNames.AuditDetailName"/>, or null when the decision used the reading of a
/// host about itself, which attests nothing: the audit detail is then left out.
/// </param>
/// <param name="Basis">What settled it.</param>
public sealed record ViabilityDecision(
    ViabilityMutation Mutation,
    ViabilityVerdict Verdict,
    ConfigurationEvidence? Evidence,
    ViabilityDecisionBasis Basis)
{
    /// <summary>True when nothing is violated.</summary>
    public bool IsAllowed => Verdict.IsAllowed;

    /// <summary>The typed refusal for this decision. Throws when the decision allows the change.</summary>
    public GovernanceConflictException Refusal() => ViabilityRefusal.For(Verdict, Mutation);
}
