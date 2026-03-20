using Apperture.Core.Application.ControlFacade;

namespace Apperture.ControlPlane.Security;

public static class ScheduleFacadeTargetProjection
{
    /// <summary>
    /// Minimal schedule targets projected from scheduled execution activity.
    /// This avoids requiring schedule persistence/read-model in this cycle.
    /// </summary>
    public static IReadOnlyList<ControlTargetSummaryDto> ProjectScheduleTargets(
        IReadOnlyList<ControlActivitySummaryDto> activity)
    {
        var scheduleIds = activity
            .Where(a => string.Equals(a.TargetKind, ControlFacade.TargetKindSchedule, StringComparison.Ordinal))
            .Select(a => a.TargetId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return scheduleIds.Select(id =>
            new ControlTargetSummaryDto(
                TargetKind: ControlFacade.TargetKindSchedule,
                TargetId: id,
                DisplayName: id,
                GroupKey: null,
                AvailableActions: Array.Empty<ControlActionAvailabilityDto>())).ToList();
    }
}

