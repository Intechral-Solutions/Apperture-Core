using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.RuntimePlane.Engine.PlatformEvents;

internal sealed class NoOpModuleTelemetry : IModuleTelemetry
{
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
