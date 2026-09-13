using System.Collections.Concurrent;

namespace Apperture.Core.Application.Notifications;

/// <summary>Process-scoped throttle tracking for rule evaluation.</summary>
public sealed class InMemoryNotificationThrottleStore : INotificationThrottleStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastMatchUtc = new(StringComparer.Ordinal);

    public ValueTask<bool> IsThrottledAsync(
        string definitionId,
        string subjectKey,
        TimeSpan minInterval,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (minInterval <= TimeSpan.Zero)
            return ValueTask.FromResult(false);

        var key = ThrottleKey(definitionId, subjectKey);
        if (!_lastMatchUtc.TryGetValue(key, out var last))
            return ValueTask.FromResult(false);

        return ValueTask.FromResult(evaluatedAtUtc - last < minInterval);
    }

    public ValueTask RecordMatchAsync(
        string definitionId,
        string subjectKey,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        _lastMatchUtc[ThrottleKey(definitionId, subjectKey)] = evaluatedAtUtc;
        return ValueTask.CompletedTask;
    }

    private static string ThrottleKey(string definitionId, string subjectKey) =>
        $"{definitionId}|{subjectKey}";
}
