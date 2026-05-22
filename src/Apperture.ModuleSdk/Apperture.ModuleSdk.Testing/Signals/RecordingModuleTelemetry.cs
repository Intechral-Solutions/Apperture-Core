using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.ModuleSdk.Testing.Signals;

/// <summary>Captures telemetry calls for harness assertions (best-effort, never throws).</summary>
public sealed class RecordingModuleTelemetry : IModuleTelemetry
{
    private readonly List<RecordedTelemetryCounter> _counters = [];
    private readonly List<MutableDurationEntry> _durations = [];
    private readonly TimeProvider _time;

    public RecordingModuleTelemetry(TimeProvider? time = null) =>
        _time = time ?? TimeProvider.System;

    public IReadOnlyList<RecordedTelemetryCounter> Counters => _counters;

    public IReadOnlyList<RecordedTelemetryDuration> Durations =>
        _durations.Select(d => d.ToRecord()).ToList();

    /// <inheritdoc />
    public void RecordCounter(string name, double value = 1, IReadOnlyDictionary<string, string>? tags = null)
    {
        try
        {
            _counters.Add(new RecordedTelemetryCounter(name, value, tags, _time.GetUtcNow()));
        }
        catch
        {
        }
    }

    /// <inheritdoc />
    public IDisposable MeasureDuration(string name, IReadOnlyDictionary<string, string>? tags = null)
    {
        try
        {
            var entry = new MutableDurationEntry(name, tags, _time.GetUtcNow());
            _durations.Add(entry);
            return new DurationScope(entry, _time);
        }
        catch
        {
            return NoopDisposable.Instance;
        }
    }

    private sealed class MutableDurationEntry(string name, IReadOnlyDictionary<string, string>? tags, DateTimeOffset started)
    {
        public string Name { get; } = name;
        public IReadOnlyDictionary<string, string>? Tags { get; } = tags;
        public DateTimeOffset StartedAtUtc { get; } = started;
        public DateTimeOffset? CompletedAtUtc { get; set; }

        public RecordedTelemetryDuration ToRecord() => new(Name, Tags, StartedAtUtc, CompletedAtUtc);
    }

    private sealed class DurationScope(MutableDurationEntry entry, TimeProvider time) : IDisposable
    {
        public void Dispose() => entry.CompletedAtUtc = time.GetUtcNow();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}
