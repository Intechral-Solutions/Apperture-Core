using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.ModuleSdk.Testing.Signals;

/// <summary>Captured operational log line from <see cref="InMemoryOperationalLogSink"/>.</summary>
public sealed record RecordedOperationalLog(
    OperationalLogLevel Level,
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Data,
    string ModuleId,
    string? IntegrationId,
    string? RunId,
    string? ScheduleId,
    string? ProcedureId,
    string? CorrelationId,
    string? CausationId,
    string? StepName);
