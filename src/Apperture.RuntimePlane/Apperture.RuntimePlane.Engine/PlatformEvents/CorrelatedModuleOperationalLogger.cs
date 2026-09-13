using Apperture.ModuleSdk.Abstractions.Signals;
using Microsoft.Extensions.Logging;

namespace Apperture.RuntimePlane.Engine.PlatformEvents;

/// <summary>
/// Wraps operational logging with execution correlation scopes and best-effort failure semantics (ADR-0038).
/// </summary>
internal sealed class CorrelatedModuleOperationalLogger : IModuleOperationalLogger
{
    private readonly ILogger? _logger;
    private readonly IModuleOperationalLogger _inner;
    private readonly IModulePlatformEmitContext _correlation;
    private readonly string? _stepName;

    public CorrelatedModuleOperationalLogger(
        IModuleOperationalLogger inner,
        IModulePlatformEmitContext correlation,
        string? stepName = null,
        ILogger? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _stepName = stepName;
        _logger = logger;
    }

    /// <inheritdoc />
    public void LogOperational(
        OperationalLogLevel level,
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        try
        {
            var enriched = EnrichData(data);
            if (_logger is not null)
            {
                using (_logger.BeginScope(BuildScopeState()))
                {
                    _inner.LogOperational(level, code, message, enriched);
                }
            }
            else
            {
                _inner.LogOperational(level, code, message, enriched);
            }
        }
        catch
        {
            // Best-effort: logging must not fail the step.
        }
    }

    private Dictionary<string, object?> EnrichData(IReadOnlyDictionary<string, object?>? data)
    {
        var enriched = data is not null
            ? new Dictionary<string, object?>(data)
            : new Dictionary<string, object?>();

        TryAdd(enriched, "moduleId", _correlation.ModuleId);
        TryAdd(enriched, "integrationId", _correlation.IntegrationId);
        TryAdd(enriched, "runId", _correlation.RunId);
        TryAdd(enriched, "scheduleId", _correlation.ScheduleId);
        TryAdd(enriched, "procedureId", _correlation.ProcedureId);
        TryAdd(enriched, "correlationId", _correlation.CorrelationId);
        TryAdd(enriched, "causationId", _correlation.CausationId);
        TryAdd(enriched, "stepName", _stepName);

        return enriched;
    }

    private Dictionary<string, object?> BuildScopeState()
    {
        var scope = new Dictionary<string, object?>(StringComparer.Ordinal);
        TryAdd(scope, "ModuleId", _correlation.ModuleId);
        TryAdd(scope, "IntegrationId", _correlation.IntegrationId);
        TryAdd(scope, "RunId", _correlation.RunId);
        TryAdd(scope, "ScheduleId", _correlation.ScheduleId);
        TryAdd(scope, "ProcedureId", _correlation.ProcedureId);
        TryAdd(scope, "CorrelationId", _correlation.CorrelationId);
        TryAdd(scope, "CausationId", _correlation.CausationId);
        TryAdd(scope, "StepName", _stepName);
        return scope;
    }

    private static void TryAdd(IDictionary<string, object?> target, string key, object? value)
    {
        if (value is not null && !target.ContainsKey(key))
            target[key] = value;
    }
}
