using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.ModuleSdk.Testing.Signals;

/// <summary>In-memory operational log capture for module harness tests (best-effort, never throws).</summary>
public sealed class InMemoryOperationalLogSink : IModuleOperationalLogger
{
    private readonly List<RecordedOperationalLog> _logs = [];
    private readonly string _moduleId;
    private readonly string? _integrationId;
    private readonly string? _runId;
    private readonly string? _scheduleId;
    private readonly string? _procedureId;
    private readonly string? _correlationId;
    private readonly string? _causationId;
    private readonly string? _stepName;

    public InMemoryOperationalLogSink(
        string moduleId,
        string? integrationId = null,
        string? runId = null,
        string? scheduleId = null,
        string? procedureId = null,
        string? correlationId = null,
        string? causationId = null,
        string? stepName = null)
    {
        _moduleId = moduleId ?? throw new ArgumentNullException(nameof(moduleId));
        _integrationId = integrationId;
        _runId = runId;
        _scheduleId = scheduleId;
        _procedureId = procedureId;
        _correlationId = correlationId;
        _causationId = causationId;
        _stepName = stepName;
    }

    public IReadOnlyList<RecordedOperationalLog> OperationalLogs => _logs;

    /// <inheritdoc />
    public void LogOperational(
        OperationalLogLevel level,
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        try
        {
            _logs.Add(new RecordedOperationalLog(
                level,
                code,
                message,
                data,
                _moduleId,
                _integrationId,
                _runId,
                _scheduleId,
                _procedureId,
                _correlationId,
                _causationId,
                _stepName));
        }
        catch
        {
            // Best-effort.
        }
    }
}
