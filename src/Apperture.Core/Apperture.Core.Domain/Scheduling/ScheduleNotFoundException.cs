namespace Apperture.Core.Domain.Scheduling;

/// <summary>
/// A schedule delete targeted a schedule that does not exist, as the delete's own transaction read it. AUD-1.4.
/// </summary>
/// <remarks>
/// Inherits <see cref="InvalidOperationException"/> so a caller that only catches the base type still fails closed. The delete
/// endpoint catches exactly this type and maps it to 404; any other failure of the delete (an audit invariant, a transaction or
/// infrastructure error) is not a "not found" and reaches the host's handler instead.
/// </remarks>
public sealed class ScheduleNotFoundException : InvalidOperationException
{
    public ScheduleNotFoundException(ScheduleId scheduleId)
        : base($"Schedule {scheduleId.Value} not found.")
    {
        ScheduleId = scheduleId;
    }

    public ScheduleId ScheduleId { get; }
}
