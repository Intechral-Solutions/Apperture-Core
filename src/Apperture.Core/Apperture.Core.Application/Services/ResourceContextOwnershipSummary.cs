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
    /// literal wording: "no Integration whose state is not Retired". This is intentionally a different
    /// predicate shape from the reactivation guard elsewhere (which fires on transitions *into* Active
    /// specifically) -- both encode the same true test, "can this resource dispatch/run", just applied
    /// to a current state here versus a transition target there. An Inactive integration cannot
    /// dispatch, exactly like a Retired one, so it must block archive the same way; but moving between
    /// Retired and Inactive makes nothing live, so neither of those transitions needs a guard -- only a
    /// transition that lands on Active does.
    /// </remarks>
    public bool BlocksArchive => LiveIntegrations + ActiveConnections + EnabledSchedules > 0;
}
