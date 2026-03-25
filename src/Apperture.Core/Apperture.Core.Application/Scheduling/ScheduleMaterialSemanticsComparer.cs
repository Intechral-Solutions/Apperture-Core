using Apperture.Core.Domain.Scheduling;

namespace Apperture.Core.Application.Scheduling;

/// <summary>
/// Detects changes that alter when a schedule would fire (Epic 66). Enable/disable is handled separately.
/// </summary>
public static class ScheduleMaterialSemanticsComparer
{
    /// <summary>
    /// True when recurrence or timezone differs between two definitions (ordinal string compare).
    /// Name, execution policy, version, and target are ignored here; target is immutable after create at API.
    /// </summary>
    public static bool HasMaterialSemanticsChange(ScheduleDefinition prior, ScheduleDefinition next) =>
        !string.Equals(prior.RecurrenceExpression, next.RecurrenceExpression, StringComparison.Ordinal)
        || !string.Equals(prior.TimeZone, next.TimeZone, StringComparison.Ordinal);
}
