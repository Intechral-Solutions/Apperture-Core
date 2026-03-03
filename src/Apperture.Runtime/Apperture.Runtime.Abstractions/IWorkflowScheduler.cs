namespace Apperture.Runtime.Abstractions;

public interface IWorkflowScheduler
{
    ValueTask ScheduleAsync(string scheduleId, DateTimeOffset dueAt, StartWorkflowRequest request, CancellationToken cancellationToken = default);
    ValueTask CancelTimerAsync(string scheduleId, CancellationToken cancellationToken = default);
}
