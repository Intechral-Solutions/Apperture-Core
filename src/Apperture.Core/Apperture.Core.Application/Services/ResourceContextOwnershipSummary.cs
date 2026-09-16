namespace Apperture.Core.Application.Services;

/// <summary>Per-family live/retired-or-disabled counts for one ResourceContext, used to decide whether it can be archived (CTX-3A §6.5).</summary>
public sealed record ResourceContextOwnershipSummary(
    int LiveIntegrations, int RetiredIntegrations,
    int ActiveConnections, int RetiredConnections,
    int EnabledSchedules, int DisabledSchedules)
{
    /// <summary>
    /// The "nothing live" archive rule (CTX-3A §6.5, decision D2): a context blocks archive only while
    /// it still owns at least one live/active/enabled resource. Retired Connections (which can only be
    /// retired, never deleted) and disabled Schedules (which can never be deleted) do not block archive.
    /// </summary>
    /// <remarks>
    /// <see cref="LiveIntegrations"/> is "count(State != Retired)" (Active AND Inactive), per §6.5's
    /// literal wording: "no Integration whose state is not Retired". This is a genuinely different
    /// predicate from the reactivation guard (<c>ResourceContextReactivationGuard</c>, in
    /// Apperture.ControlPlane.Security) -- they serve two different purposes, not one shared test. This
    /// rule (archive-blocking) exists so an operator must explicitly retire every remaining Integration
    /// before archiving its context: a cleanup/visibility gate over CURRENT state, where Inactive counts
    /// exactly like Active because neither is Retired. The reactivation guard is a narrower, separate
    /// concern: it originally fired only on transitions *landing on* Active ("can this resource dispatch
    /// again"), which is a statement about a TRANSITION, not current state.
    /// <para>
    /// The CTX-3B archive-concurrency integrity pass discovered these two predicates can disagree:
    /// Retired -&gt; Inactive is not a "can dispatch" transition (so the original reactivation guard
    /// never covered it), but it DOES flip <see cref="BlocksArchive"/> from false to true for the owning
    /// context, exactly like a transition into Active would. Left unguarded, that transition could race
    /// a concurrent Archive the same way an Active reactivation could. The reactivation guard was
    /// therefore widened to also cover Retired -&gt; Inactive, closing that gap -- see
    /// <c>ResourceContextReactivationGuard</c>'s own remarks for the full enumerated list of guarded
    /// transitions.
    /// </para>
    /// </remarks>
    public bool BlocksArchive => LiveIntegrations + ActiveConnections + EnabledSchedules > 0;
}
