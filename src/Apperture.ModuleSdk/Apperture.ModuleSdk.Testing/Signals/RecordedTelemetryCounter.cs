namespace Apperture.ModuleSdk.Testing.Signals;

public sealed record RecordedTelemetryCounter(
    string Name,
    double Value,
    IReadOnlyDictionary<string, string>? Tags,
    DateTimeOffset RecordedAtUtc);
