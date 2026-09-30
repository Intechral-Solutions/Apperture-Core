namespace Apperture.Core.Domain.Scheduling;

/// <summary>
/// An authorized delete targeted a schedule that is still enabled. LFC-1.1.
/// </summary>
/// <remarks>
/// Inherits <see cref="InvalidOperationException"/> so a caller that only catches the base type still
/// fails closed. Controllers catch this type first and map it to 409; a plain
/// <see cref="InvalidOperationException"/> from delete means the schedule does not exist (404).
/// </remarks>
public sealed class ScheduleDeleteRejectedException : InvalidOperationException
{
    public const string EnabledMessage = "Cannot delete schedule: it is still enabled. Disable it first.";

    public ScheduleDeleteRejectedException(ScheduleId scheduleId)
        : base(EnabledMessage)
    {
        ScheduleId = scheduleId;
    }

    public ScheduleId ScheduleId { get; }
}
