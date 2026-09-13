using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.RuntimePlane.Engine.PlatformEvents;

/// <summary>Default telemetry implementation (best-effort no-op) until OpenTelemetry adapter ships.</summary>
public sealed class NoOpModuleTelemetry : IModuleTelemetry
{
    public static NoOpModuleTelemetry Instance { get; } = new();

    private NoOpModuleTelemetry()
    {
    }

    /// <inheritdoc />
    public void RecordCounter(string name, double value = 1, IReadOnlyDictionary<string, string>? tags = null)
    {
    }

    public IDisposable MeasureDuration(string name, IReadOnlyDictionary<string, string>? tags = null) =>
        NoopDisposable.Instance;

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose()
        {
        }
    }
}
