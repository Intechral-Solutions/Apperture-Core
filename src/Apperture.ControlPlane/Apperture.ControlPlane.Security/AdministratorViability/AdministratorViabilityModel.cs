using Apperture.Core.Application.AdministratorViability;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>
/// Which authentication the evaluation describes (IAM-10.2b, ruling D-4). A configuration change does not reach the running
/// authentication middleware until the host restarts, so a change can be safe in one view and not in the other. Every
/// invariant is applied to each view independently.
/// </summary>
public enum ViabilityView
{
    /// <summary>What the running host actually accepts: its own options, and the live tenant check at sign-in.</summary>
    Now,

    /// <summary>What the persisted configuration will accept once the host has restarted.</summary>
    Restart,
}

/// <summary>How far a reading of the deployment configuration can be relied on.</summary>
public enum ConfigurationTrustLevel
{
    /// <summary>The reading is authoritative for this installation.</summary>
    Trusted,

    /// <summary>The reading is missing or incomplete, so it cannot be established.</summary>
    Unknown,

    /// <summary>The reading is present and cannot be believed (unparseable, or failed validation).</summary>
    Untrusted,
}

/// <summary>
/// Whether the configuration inputs of an evaluation can be relied on, per input. Anything that is not trusted fails closed for a
/// viability-reducing operation (ruling D-6).
/// </summary>
/// <param name="Local">Whether Local sign-in is enabled for the installation.</param>
/// <param name="Entra">Whether the Entra options, including the client secret the active registration needs, are known.</param>
/// <param name="Reasons">Plain-language reasons for anything not trusted, for the operator.</param>
public sealed record ConfigurationTrust(
    ConfigurationTrustLevel Local,
    ConfigurationTrustLevel Entra,
    IReadOnlyList<string> Reasons)
{
    /// <summary>Everything is established.</summary>
    public static ConfigurationTrust Full { get; } = new(ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Trusted, []);

    /// <summary>True when Local sign-in availability is established.</summary>
    public bool LocalKnown => Local == ConfigurationTrustLevel.Trusted;

    /// <summary>True when Entra availability is established.</summary>
    public bool EntraKnown => Entra == ConfigurationTrustLevel.Trusted;
}

/// <summary>What one view of the authentication accepts, as far as administrator sign-in is concerned.</summary>
/// <param name="View">Which view.</param>
/// <param name="LocalSignInEnabled">Local sign-in is enabled. The direct Local login route depends on nothing else: the chooser flags never matter.</param>
/// <param name="EntraSignInUsable">Entra sign-in is enabled and its options are valid.</param>
/// <param name="AcceptedEntraTenantId">
/// The directory id a token must carry to be accepted, or null when no Entra token is accepted at all (no active registration,
/// an unresolvable tenant, or a running handler bound to a different tenant than the persisted registration).
/// </param>
/// <param name="IsEstimate">The view is inferred (the emergency command line cannot observe a running host's options).</param>
/// <param name="Notes">Anything the evaluation found worth reporting, such as two readers of the running options that disagree.</param>
public sealed record SignInAvailability(
    ViabilityView View,
    bool LocalSignInEnabled,
    bool EntraSignInUsable,
    string? AcceptedEntraTenantId,
    bool IsEstimate,
    IReadOnlyList<string> Notes);

/// <summary>The two views and the trust in the inputs that produced them.</summary>
public sealed record EffectiveSignInConfiguration(
    SignInAvailability Now,
    SignInAvailability Restart,
    ConfigurationTrust Trust);

/// <summary>The administrators who can sign in under one view.</summary>
/// <param name="View">Which view.</param>
/// <param name="ViableOrdinaryAdministrators">
/// VOA: holders of the Platform Administrator role who are not Recovery Access and have at least one usable sign-in method.
/// </param>
/// <param name="EntraIndependentAdministrators">
/// EI: the VOA who have an enabled Local credential while Local sign-in is enabled, so they do not depend on Entra.
/// </param>
public sealed record AdministratorViabilitySnapshot(
    ViabilityView View,
    IReadOnlySet<string> ViableOrdinaryAdministrators,
    IReadOnlySet<string> EntraIndependentAdministrators);

/// <summary>Everything the invariants are stated over, read in one state.</summary>
/// <param name="Now">Viability under the running host.</param>
/// <param name="Restart">Viability under the persisted configuration after a restart.</param>
/// <param name="RecoveryHealthy">Recovery Access is demonstrably healthy. It does not depend on the view.</param>
/// <param name="Trust">The trust in the configuration inputs.</param>
/// <param name="Configuration">The two availabilities the snapshots were evaluated under.</param>
/// <param name="Administrators">
/// The stored facts the snapshots were derived from: every holder of the Platform Administrator role. Null only for a state that
/// was built without them (a test double); a state read from the database always carries them. A decision that compares facts
/// treats null as "cannot be compared", never as "unchanged".
/// </param>
/// <param name="Recovery">The Recovery Access posture inputs the health was derived from, with the same rule for null.</param>
public sealed record AdministratorViabilityState(
    AdministratorViabilitySnapshot Now,
    AdministratorViabilitySnapshot Restart,
    bool RecoveryHealthy,
    ConfigurationTrust Trust,
    EffectiveSignInConfiguration Configuration,
    IReadOnlyList<AdministratorFacts>? Administrators = null,
    RecoveryAccessFacts? Recovery = null)
{
    /// <summary>
    /// True only when nothing a viability definition reads differs between the two states: the stored facts are identical (see
    /// <see cref="FactsUnchanged"/>) <i>and</i> so is the effective configuration the snapshots were evaluated under, in both
    /// views, with the same trust. A change that moves the configuration (a registration or sign-in policy writer) is therefore
    /// never "unchanged", however few facts it touches, and a change that moves only facts is judged on them.
    /// </summary>
    public static bool InputsUnchanged(AdministratorViabilityState pre, AdministratorViabilityState post) =>
        FactsUnchanged(pre, post)
        && SameAvailability(pre.Configuration.Now, post.Configuration.Now)
        && SameAvailability(pre.Configuration.Restart, post.Configuration.Restart)
        && pre.Trust.Local == post.Trust.Local
        && pre.Trust.Entra == post.Trust.Entra;

    private static bool SameAvailability(SignInAvailability a, SignInAvailability b) =>
        a.View == b.View
        && a.LocalSignInEnabled == b.LocalSignInEnabled
        && a.EntraSignInUsable == b.EntraSignInUsable
        && string.Equals(a.AcceptedEntraTenantId, b.AcceptedEntraTenantId, StringComparison.Ordinal);

    /// <summary>
    /// True only when both states carry their stored facts and those facts are identical: the same holders of the role, each with
    /// the same Local link, credential, proven Entra tenants and Recovery Access flag, and the same Recovery Access posture inputs.
    /// </summary>
    public static bool FactsUnchanged(AdministratorViabilityState pre, AdministratorViabilityState post)
    {
        if (pre.Administrators is null || post.Administrators is null || pre.Recovery is null || post.Recovery is null)
            return false;
        if (pre.Recovery != post.Recovery || pre.Administrators.Count != post.Administrators.Count)
            return false;

        var before = pre.Administrators.OrderBy(a => a.UserId, StringComparer.Ordinal).ToList();
        var after = post.Administrators.OrderBy(a => a.UserId, StringComparer.Ordinal).ToList();
        for (var i = 0; i < before.Count; i++)
        {
            var (a, b) = (before[i], after[i]);
            if (!string.Equals(a.UserId, b.UserId, StringComparison.Ordinal)
                || a.IsRecoveryAccess != b.IsRecoveryAccess
                || a.HasLocalLink != b.HasLocalLink
                || a.LocalCredentialEnabled != b.LocalCredentialEnabled
                || !a.ProvenEntraLinkTenantIds.OrderBy(t => t, StringComparer.Ordinal).SequenceEqual(b.ProvenEntraLinkTenantIds.OrderBy(t => t, StringComparer.Ordinal), StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The snapshot of one view.</summary>
    public AdministratorViabilitySnapshot For(ViabilityView view) => view == ViabilityView.Now ? Now : Restart;
}

/// <summary>
/// Whether a raise-only writer's own change reduced the viability of the user it changed (IAM-10.2b WP2 review, M-2). It does not
/// compare two evaluations of the whole system: that would hold the writer responsible for a configuration reload, a Recovery
/// Access recovery code consumed at sign-in (ruling C-14), another administrator's change by a writer not yet under the gate, or
/// the Recovery Access designation. It compares the affected user's stored facts before and after, judged under <i>one</i>
/// configuration, the one captured before the change.
/// </summary>
public static class RaiseOnlyAttribution
{
    /// <summary>
    /// A description of the reduction, or null when the affected user's contribution to VOA and EI is no smaller in either view.
    /// A user who held no administrator role before had nothing to lose. The Recovery Access flag is taken from before the
    /// change: a designation is not this writer's act.
    /// </summary>
    public static string? FindReduction(AdministratorViabilityState before, AdministratorViabilityState after, string affectedUserId)
    {
        if (before.Administrators is null || after.Administrators is null)
        {
            // A state built without its facts (a test double) can only be compared by membership of the user in each view.
            foreach (var view in new[] { ViabilityView.Now, ViabilityView.Restart })
            {
                var (b, a) = (before.For(view), after.For(view));
                if (b.ViableOrdinaryAdministrators.Contains(affectedUserId) && !a.ViableOrdinaryAdministrators.Contains(affectedUserId))
                    return $"the user is no longer a viable administrator ({view})";
                if (b.EntraIndependentAdministrators.Contains(affectedUserId) && !a.EntraIndependentAdministrators.Contains(affectedUserId))
                    return $"the user is no longer an Entra-independent administrator ({view})";
            }

            return null;
        }

        var beforeFacts = before.Administrators.FirstOrDefault(f => string.Equals(f.UserId, affectedUserId, StringComparison.Ordinal));
        if (beforeFacts is null)
            return null;

        var afterFacts = after.Administrators.FirstOrDefault(f => string.Equals(f.UserId, affectedUserId, StringComparison.Ordinal));
        foreach (var (view, availability) in new[]
                 {
                     (ViabilityView.Now, before.Configuration.Now),
                     (ViabilityView.Restart, before.Configuration.Restart),
                 })
        {
            var held = AdministratorViabilityEvaluator.Evaluate([beforeFacts], availability);
            var kept = afterFacts is null
                ? new AdministratorViabilitySnapshot(view, new HashSet<string>(), new HashSet<string>())
                : AdministratorViabilityEvaluator.Evaluate([afterFacts with { IsRecoveryAccess = beforeFacts.IsRecoveryAccess }], availability);

            if (held.ViableOrdinaryAdministrators.Contains(affectedUserId) && !kept.ViableOrdinaryAdministrators.Contains(affectedUserId))
                return $"the user is no longer a viable administrator ({view})";
            if (held.EntraIndependentAdministrators.Contains(affectedUserId) && !kept.EntraIndependentAdministrators.Contains(affectedUserId))
                return $"the user is no longer an Entra-independent administrator ({view})";
        }

        return null;
    }
}
