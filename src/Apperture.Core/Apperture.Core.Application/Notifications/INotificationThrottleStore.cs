namespace Apperture.Core.Application.Notifications;

public interface INotificationThrottleStore
{
    /// <summary>Returns true when the match should be suppressed by throttle policy.</summary>
    ValueTask<bool> IsThrottledAsync(
        string definitionId,
        string subjectKey,
        TimeSpan minInterval,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Records a successful match for throttle tracking (evaluate-only).</summary>
    ValueTask RecordMatchAsync(
        string definitionId,
        string subjectKey,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken = default);
}
