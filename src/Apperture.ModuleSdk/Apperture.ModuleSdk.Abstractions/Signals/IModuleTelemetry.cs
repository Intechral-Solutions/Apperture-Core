namespace Apperture.ModuleSdk.Abstractions.Signals;

/// <summary>Optional metrics facade; no-op in Phase 2 (Epic 77).</summary>
public interface IModuleTelemetry
{
    void RecordCounter(string name, double value = 1, IReadOnlyDictionary<string, string>? tags = null);

    IDisposable MeasureDuration(string name, IReadOnlyDictionary<string, string>? tags = null);
}
