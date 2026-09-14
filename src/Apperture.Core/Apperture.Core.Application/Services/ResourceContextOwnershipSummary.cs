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
    public bool BlocksArchive => LiveIntegrations + ActiveConnections + EnabledSchedules > 0;
}
