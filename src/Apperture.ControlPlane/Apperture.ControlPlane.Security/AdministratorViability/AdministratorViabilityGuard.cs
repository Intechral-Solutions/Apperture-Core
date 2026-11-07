using Apperture.Core.Application.AdministratorViability;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>What a viability decision found.</summary>
public enum ViabilityVerdictKind
{
    /// <summary>No invariant is newly violated.</summary>
    Allowed,

    /// <summary>I-VIABLE: the mutation would eliminate the last viable ordinary Platform Administrator.</summary>
    LastViableAdministrator,

    /// <summary>
    /// I-FALLBACK (or, for a revocation, ruling C-13-R): the mutation would leave no viable Entra-independent ordinary
    /// administrator while Recovery Access cannot stand in.
    /// </summary>
    EntraIndependentFallbackRequired,

    /// <summary>
    /// The deployment configuration the decision depends on is unknown or untrustworthy, so the decision fails closed. Only a
    /// viability-reducing operation is ever given this verdict.
    /// </summary>
    ConfigurationUnavailable,

    /// <summary>
    /// An authenticator replacement would leave the Recovery Access posture worse than it was: not healthy after being healthy, or having
    /// lost what it held. It depends on no configuration.
    /// </summary>
    RecoveryPostureReduced,
}

/// <summary>A viability decision. When it is a refusal, the view and a plain-language detail say why.</summary>
public sealed record ViabilityVerdict(ViabilityVerdictKind Kind, ViabilityView? View, string Detail)
{
    /// <summary>The mutation may proceed.</summary>
    public static ViabilityVerdict Allowed { get; } = new(ViabilityVerdictKind.Allowed, null, "");

    /// <summary>True when nothing is violated.</summary>
    public bool IsAllowed => Kind == ViabilityVerdictKind.Allowed;
}

/// <summary>
/// The two administrator-viability invariants as pure predicates over a state before and after a mutation (IAM-10 design gate
/// §3.2, Amendment A). Both have one refusal shape: <i>a mutation is refused iff it moves the system from a state that satisfies
/// the invariant to one that does not</i>. A state that already violates an invariant is never a reason to refuse an unrelated or
/// repairing change. Each invariant is applied to the Now view and the Restart view independently (ruling D-4).
/// </summary>
public static class AdministratorViabilityGuard
{
    /// <summary>
    /// <b>I-VIABLE</b>: refused iff VOA(pre) ≥ 1 and VOA(post) = 0. <b>I-FALLBACK</b>: satisfied when Recovery Access is healthy
    /// or EI ≥ 1; refused iff satisfied before and not after. Fails closed when the configuration cannot be relied on.
    /// </summary>
    public static ViabilityVerdict Evaluate(AdministratorViabilityState pre, AdministratorViabilityState post) =>
        Judge(pre, post).Verdict;

    private static (ViabilityVerdict Verdict, ConfigurationEvidence? Evidence, ViabilityDecisionBasis Basis) Judge(
        AdministratorViabilityState pre,
        AdministratorViabilityState post)
    {
        // A change that left every input the definitions read exactly as it was (every stored fact, and the configuration they are
        // judged under) cannot have reduced anything, so it needs no trusted configuration to be allowed: an ordinary user's
        // credential or link. The comparison is of what was read under the gate before and after the change, in the writer's own
        // transaction. A state without facts, or a change to any input, goes on to the full evaluation, which fails closed.
        if (AdministratorViabilityState.InputsUnchanged(pre, post))
            return (ViabilityVerdict.Allowed, ConfigurationEvidence.NotRequired, ViabilityDecisionBasis.InputsUnchanged);

        // A result that still holds an Entra-independent ordinary administrator in both views satisfies both invariants whatever
        // Microsoft sign-in can do (EI is a subset of VOA, and an EI administrator makes the fallback invariant hold), so an Entra
        // reading that cannot be established (a secret store that cannot be read) is no reason to refuse it.
        if (ProvesSafeWithoutEntra(post))
            return (ViabilityVerdict.Allowed, EvidenceUsed(pre, post), ViabilityDecisionBasis.LocalSignIn);

        var unavailable = RequireTrust(pre, post, needEntra: true);
        return unavailable is not null
            ? (unavailable, ConfigurationEvidence.Unavailable, ViabilityDecisionBasis.ConfigurationUnavailable)
            : (Detect(pre, post), EvidenceUsed(pre, post), ViabilityDecisionBasis.FullEvaluation);
    }

    /// <summary>
    /// A Recovery Access designation: the two invariants in both views, on the Local and the Microsoft readings, unless the inputs did
    /// not change or the result is proven safe without Microsoft. It is <see cref="Evaluate"/>, with the class of evidence it used.
    /// </summary>
    public static ViabilityDecision DecideDesignation(AdministratorViabilityState pre, AdministratorViabilityState post)
    {
        var (verdict, evidence, basis) = Judge(pre, post);
        return new ViabilityDecision(ViabilityMutation.RecoveryAccessDesignation, verdict, evidence, basis);
    }

    /// <summary>
    /// A strict revocation: <see cref="EvaluateRevocation"/>, with the class of evidence it used. It needs the Local reading and never
    /// the Microsoft one, so a host that has not yet built its Microsoft sign-in options does not stop it.
    /// </summary>
    public static ViabilityDecision DecideRevocation(AdministratorViabilityState post, string revokedUserId)
    {
        var verdict = EvaluateRevocation(post, revokedUserId);
        return verdict.Kind == ViabilityVerdictKind.ConfigurationUnavailable
            ? new ViabilityDecision(ViabilityMutation.RecoveryAccessRevocation, verdict, ConfigurationEvidence.Unavailable, ViabilityDecisionBasis.ConfigurationUnavailable)
            : new ViabilityDecision(ViabilityMutation.RecoveryAccessRevocation, verdict, EvidenceUsed(post, post), ViabilityDecisionBasis.LocalSignIn);
    }

    /// <summary>
    /// A bare reset of <paramref name="userId"/>'s authenticator. It is judged on what it can do, and it can do one thing: take a healthy
    /// Recovery Access posture away. No stored fact of any administrator depends on an authenticator, so I-VIABLE cannot be affected.
    /// <list type="bullet">
    /// <item>The account is not the Recovery Access account, or the posture was not healthy, or it still is: it cannot have reduced
    /// anything, and no configuration is needed (<see cref="ConfigurationEvidence.NotRequired"/>).</item>
    /// <item>It took a healthy posture away: I-FALLBACK then needs an Entra-independent ordinary administrator in each view, which is the
    /// stored Local facts under each view's Local setting. The Microsoft reading is never asked, so what Microsoft sign-in can do cannot
    /// refuse it and cannot allow it. A Local setting that is not established refuses it as unavailable.</item>
    /// <item>Anything other than the authenticator changed: this predicate does not apply, and the change goes to the full evaluation.</item>
    /// </list>
    /// </summary>
    public static ViabilityDecision DecideBareReset(AdministratorViabilityState pre, AdministratorViabilityState post, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        const ViabilityMutation Mutation = ViabilityMutation.AuthenticatorReset;

        if (!OnlyAuthenticatorChanged(pre, post, userId, resetOnly: true))
            return FullEvaluation(Mutation, pre, post);

        var (before, after) = (pre.Recovery!, post.Recovery!);
        if (!IsTheRecoveryAccount(before, userId) || !before.IsHealthy || after.IsHealthy)
            return PostureAllows(Mutation);

        if (!pre.Trust.LocalKnown || !post.Trust.LocalKnown)
        {
            return new ViabilityDecision(
                Mutation,
                Unavailable(pre.Trust.LocalKnown ? post.Trust : pre.Trust),
                ConfigurationEvidence.Unavailable,
                ViabilityDecisionBasis.ConfigurationUnavailable);
        }

        foreach (var view in Views)
        {
            var derived = AdministratorViabilityEvaluator.Evaluate(post.Administrators!, view == ViabilityView.Now ? post.Configuration.Now : post.Configuration.Restart);

            // A snapshot that claims more, or less, than the facts prove is not believed.
            if (!derived.EntraIndependentAdministrators.SetEquals(post.For(view).EntraIndependentAdministrators))
                return FullEvaluation(Mutation, pre, post);

            if (derived.EntraIndependentAdministrators.Count == 0)
            {
                return new ViabilityDecision(
                    Mutation,
                    new ViabilityVerdict(
                        ViabilityVerdictKind.EntraIndependentFallbackRequired,
                        view,
                        "Recovery Access would stop being healthy, and no ordinary Platform Administrator could then sign in without Entra."),
                    EvidenceUsed(pre, post),
                    ViabilityDecisionBasis.LocalSignIn);
            }
        }

        return new ViabilityDecision(Mutation, ViabilityVerdict.Allowed, EvidenceUsed(pre, post), ViabilityDecisionBasis.LocalSignIn);
    }

    /// <summary>
    /// The non-destructive replacement of <paramref name="userId"/>'s authenticator, judged on its net effect on the posture alone. It
    /// stands when the account is not the Recovery Access account, when the posture is healthy afterwards, or when it was not healthy
    /// before and has lost nothing it held. It reads no configuration, so no evidence can refuse it. A replacement that took a healthy
    /// posture away, or lost what an unhealthy one held, is refused whatever else is true: a reset is a different operation with its own
    /// rule. A change to anything but the authenticator goes to the full evaluation instead.
    /// </summary>
    public static ViabilityDecision DecideReplacement(AdministratorViabilityState pre, AdministratorViabilityState post, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        const ViabilityMutation Mutation = ViabilityMutation.AuthenticatorReplacement;

        if (!OnlyAuthenticatorChanged(pre, post, userId, resetOnly: false))
            return FullEvaluation(Mutation, pre, post);

        var (before, after) = (pre.Recovery!, post.Recovery!);
        if (!IsTheRecoveryAccount(before, userId) || after.IsHealthy || (!before.IsHealthy && LostNothing(before, after)))
            return PostureAllows(Mutation);

        return new ViabilityDecision(
            Mutation,
            new ViabilityVerdict(
                ViabilityVerdictKind.RecoveryPostureReduced,
                null,
                "The replacement would leave the Recovery Access posture worse than it was."),
            ConfigurationEvidence.NotRequired,
            ViabilityDecisionBasis.PostureOnly);
    }

    private static ViabilityDecision PostureAllows(ViabilityMutation mutation) =>
        new(mutation, ViabilityVerdict.Allowed, ConfigurationEvidence.NotRequired, ViabilityDecisionBasis.PostureOnly);

    private static ViabilityDecision FullEvaluation(ViabilityMutation mutation, AdministratorViabilityState pre, AdministratorViabilityState post)
    {
        var (verdict, evidence, basis) = Judge(pre, post);
        return new ViabilityDecision(mutation, verdict, evidence, basis);
    }

    private static bool IsTheRecoveryAccount(RecoveryAccessFacts recovery, string userId) =>
        string.Equals(recovery.DesignatedUserId, userId, StringComparison.Ordinal);

    private static bool LostNothing(RecoveryAccessFacts before, RecoveryAccessFacts after) =>
        (!before.NativeMfaEnrolled || after.NativeMfaEnrolled)
        && (before.UnusedRecoveryCodes == 0 || after.UnusedRecoveryCodes > 0);

    /// <summary>
    /// True only when a posture-only predicate may be used at all: both states carry their stored facts and say of themselves what those
    /// facts say, no administrator's facts moved, and the Recovery Access posture differs at most in the authenticator. For the account
    /// the change is about, the authenticator may change (<paramref name="resetOnly"/>: it may only lose). For any other account, the
    /// Recovery Access account's authenticator did not change and the only difference allowed is recovery codes going down, which is a
    /// legitimate sign-in consuming them (it takes no gate, and this writer cannot have caused it).
    /// </summary>
    private static bool OnlyAuthenticatorChanged(AdministratorViabilityState pre, AdministratorViabilityState post, string userId, bool resetOnly)
    {
        if (pre.Recovery is not { } before || post.Recovery is not { } after)
            return false;
        if (pre.RecoveryHealthy != before.IsHealthy || post.RecoveryHealthy != after.IsHealthy)
            return false;
        if (!AdministratorViabilityState.AdministratorsUnchanged(pre, post))
            return false;
        if (!string.Equals(before.DesignatedUserId, after.DesignatedUserId, StringComparison.Ordinal)
            || before.HoldsAdministratorRole != after.HoldsAdministratorRole
            || before.HasLocalLink != after.HasLocalLink
            || before.LocalCredentialEnabled != after.LocalCredentialEnabled)
        {
            return false;
        }

        if (!IsTheRecoveryAccount(before, userId))
            return before.NativeMfaEnrolled == after.NativeMfaEnrolled && after.UnusedRecoveryCodes <= before.UnusedRecoveryCodes;

        return !resetOnly
            || ((before.NativeMfaEnrolled || !after.NativeMfaEnrolled) && after.UnusedRecoveryCodes <= before.UnusedRecoveryCodes);
    }

    /// <summary>
    /// The class of the evidence a decision that read the configuration used: the reading's own class, the same in both states when they
    /// come from one attempt. Null when the reader is a host reading its own options, which attests nothing. A decision that read no
    /// configuration never comes here: it is <see cref="ConfigurationEvidence.NotRequired"/>.
    /// </summary>
    private static ConfigurationEvidence? EvidenceUsed(AdministratorViabilityState pre, AdministratorViabilityState post)
    {
        if (pre.Trust.Evidence == ConfigurationEvidence.Unavailable || post.Trust.Evidence == ConfigurationEvidence.Unavailable)
            return ConfigurationEvidence.Unavailable;
        return pre.Trust.Evidence ?? post.Trust.Evidence;
    }

    /// <summary>
    /// True only when <paramref name="post"/>, the state a change <i>produced</i>, independently proves I-VIABLE and I-FALLBACK
    /// without any Microsoft sign-in: Local sign-in is established, the stored facts and the Recovery Access posture inputs are
    /// present (read from the database, never a test double's bare snapshot), and, applying the one definition of an
    /// Entra-independent administrator to those facts under each view's own availability, at least one ordinary (not Recovery
    /// Access) administrator with an enabled Local route, credential and identity link exists in the Now view <i>and</i> in the
    /// Restart view. The state's own snapshots must say the same, so a snapshot that claims more, or less, than the facts prove is
    /// not believed. It never reads the Entra trust, the pre-state, an aggregate count of role holders or Recovery Access health.
    /// </summary>
    public static bool ProvesSafeWithoutEntra(AdministratorViabilityState post)
    {
        if (!post.Trust.LocalKnown || post.Administrators is null || post.Recovery is null)
            return false;

        foreach (var view in Views)
        {
            var availability = view == ViabilityView.Now ? post.Configuration.Now : post.Configuration.Restart;
            var derived = AdministratorViabilityEvaluator.Evaluate(post.Administrators, availability);
            var reported = post.For(view);
            if (derived.EntraIndependentAdministrators.Count == 0
                || !derived.EntraIndependentAdministrators.SetEquals(reported.EntraIndependentAdministrators)
                || !derived.ViableOrdinaryAdministrators.SetEquals(reported.ViableOrdinaryAdministrators))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The same predicates without the trust check. It is what a raise-only writer asserts (it must find nothing), and what a test
    /// reads to see the decision independent of the configuration source.
    /// </summary>
    public static ViabilityVerdict Detect(AdministratorViabilityState pre, AdministratorViabilityState post)
    {
        foreach (var view in Views)
        {
            if (pre.For(view).ViableOrdinaryAdministrators.Count >= 1 && post.For(view).ViableOrdinaryAdministrators.Count == 0)
            {
                return new ViabilityVerdict(
                    ViabilityVerdictKind.LastViableAdministrator,
                    view,
                    "The change would leave no ordinary Platform Administrator able to sign in.");
            }
        }

        foreach (var view in Views)
        {
            if (FallbackSatisfied(pre, view) && !FallbackSatisfied(post, view))
            {
                return new ViabilityVerdict(
                    ViabilityVerdictKind.EntraIndependentFallbackRequired,
                    view,
                    "Recovery Access is not demonstrably healthy, and the change would leave no ordinary Platform Administrator who can sign in without Entra.");
            }
        }

        return ViabilityVerdict.Allowed;
    }

    /// <summary>
    /// <b>C-13-R</b>, the strict rule for revoking Recovery Access: refused unless, once the designation is cleared, some
    /// Entra-independent ordinary administrator <i>other than</i> <paramref name="revokedUserId"/> exists. It does not depend on the
    /// state before and the confirmation flag does not override it. The revoked account is excluded because revoking changes only
    /// its designation: it keeps its role and credential, and would otherwise count as the very fallback the rule requires.
    /// </summary>
    public static ViabilityVerdict EvaluateRevocation(AdministratorViabilityState post, string revokedUserId)
    {
        if (!post.Trust.LocalKnown)
            return Unavailable(post.Trust);

        foreach (var view in Views)
        {
            var others = post.For(view).EntraIndependentAdministrators.Count(id => !string.Equals(id, revokedUserId, StringComparison.Ordinal));
            if (others == 0)
            {
                return new ViabilityVerdict(
                    ViabilityVerdictKind.EntraIndependentFallbackRequired,
                    view,
                    "Revoking Recovery Access needs another ordinary Platform Administrator who can sign in without Entra. Provide one, then revoke.");
            }
        }

        return ViabilityVerdict.Allowed;
    }

    private static readonly ViabilityView[] Views = [ViabilityView.Now, ViabilityView.Restart];

    private static bool FallbackSatisfied(AdministratorViabilityState state, ViabilityView view) =>
        state.RecoveryHealthy || state.For(view).EntraIndependentAdministrators.Count >= 1;

    private static ViabilityVerdict? RequireTrust(AdministratorViabilityState pre, AdministratorViabilityState post, bool needEntra)
    {
        var preOk = pre.Trust.LocalKnown && (!needEntra || pre.Trust.EntraKnown);
        var postOk = post.Trust.LocalKnown && (!needEntra || post.Trust.EntraKnown);
        if (preOk && postOk)
            return null;
        return Unavailable(preOk ? post.Trust : pre.Trust);
    }

    private static ViabilityVerdict Unavailable(ConfigurationTrust trust) =>
        new(
            ViabilityVerdictKind.ConfigurationUnavailable,
            null,
            trust.Reasons.Count == 0
                ? "The installation's authentication configuration could not be established, so the change cannot be checked."
                : string.Join(" ", trust.Reasons));
}
