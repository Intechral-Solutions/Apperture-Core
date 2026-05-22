namespace Apperture.ModuleSdk.Testing.Signals;

public sealed record RecordedTelemetryDuration(
    string Name,
    IReadOnlyDictionary<string, string>? Tags,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);
