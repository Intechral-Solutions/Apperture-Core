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
    public static ViabilityVerdict Evaluate(AdministratorViabilityState pre, AdministratorViabilityState post)
    {
        // A change that left every input the definitions read exactly as it was (every stored fact, and the configuration they are
        // judged under) cannot have reduced anything, so it needs no trusted configuration to be allowed: an ordinary user's
        // credential or link. The comparison is of what was read under the gate before and after the change, in the writer's own
        // transaction. A state without facts, or a change to any input, goes on to the full evaluation, which fails closed.
        if (AdministratorViabilityState.InputsUnchanged(pre, post))
            return ViabilityVerdict.Allowed;

        // A result that still holds an Entra-independent ordinary administrator in both views satisfies both invariants whatever
        // Microsoft sign-in can do (EI is a subset of VOA, and an EI administrator makes the fallback invariant hold), so an Entra
        // reading that cannot be established (a secret store that cannot be read) is no reason to refuse it.
        if (ProvesSafeWithoutEntra(post))
            return ViabilityVerdict.Allowed;

        var unavailable = RequireTrust(pre, post, needEntra: true);
        return unavailable ?? Detect(pre, post);
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
