using System.Text.Json;
using Apperture.Core.Domain.Workflow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.Core.Application.PlatformEvents;

/// <summary>
/// Best-effort platform-owned observer for terminal workflow/operation runs (Epic 103D).
/// Does not affect run terminal state when emission fails.
/// </summary>
public sealed class ExecutionLifecyclePlatformEventEmitter
{
    private const int FailureExcerptMaxLength = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IPlatformEventEmitter _emitter;
    private readonly IOptions<PlatformEventsOptions> _options;
    private readonly ILogger<ExecutionLifecyclePlatformEventEmitter> _logger;

    public ExecutionLifecyclePlatformEventEmitter(
        IPlatformEventEmitter emitter,
        IOptions<PlatformEventsOptions> options,
        ILogger<ExecutionLifecyclePlatformEventEmitter> logger)
    {
        _emitter = emitter;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Emits <see cref="PlatformEventTypes.ExecutionRunCompleted"/> or
    /// <see cref="PlatformEventTypes.ExecutionRunFailed"/> after terminal state is durable.
    /// </summary>
    public async ValueTask TryEmitTerminalAsync(
        ExecutionLifecycleTerminalContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Enabled || !_options.Value.ExecutionLifecycle.Enabled)
            return;

        if (!IsTerminalStatusSupported(context.TerminalStatus))
            return;

        try
        {
            var (eventType, severity) = ResolveTypeAndSeverity(context.TerminalStatus);
            var dataJson = BuildDataJson(context);
            var dedupeKey = $"{context.RunId}:terminal:{context.TerminalStatus.ToLowerInvariant()}";
            var subject = $"run:{context.RunId}";

            var request = new PlatformEventEmitRequest(
                eventType,
                PlatformEventSources.PlatformRuntime,
                dataJson,
                context.CompletedAtUtc,
                severity,
                Subject: subject,
                DedupeKey: dedupeKey,
                IntegrationId: context.IntegrationId,
                ModuleId: context.ModuleId,
                ProcedureId: context.ProcedureId,
                RunId: context.RunId,
                ScheduleId: context.ScheduleId,
                CorrelationId: context.CorrelationId,
                CausationId: context.CausationId);

            await _emitter.EmitAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Execution lifecycle platform event emit failed (best-effort). RunId={RunId} TerminalStatus={TerminalStatus}",
                context.RunId,
                context.TerminalStatus);
        }
    }

    private static bool IsTerminalStatusSupported(string terminalStatus) =>
        string.Equals(terminalStatus, WorkflowLifecycleStatusNames.Completed, StringComparison.Ordinal)
        || string.Equals(terminalStatus, WorkflowLifecycleStatusNames.Failed, StringComparison.Ordinal);

    private static (string Type, string Severity) ResolveTypeAndSeverity(string terminalStatus) =>
        string.Equals(terminalStatus, WorkflowLifecycleStatusNames.Completed, StringComparison.Ordinal)
            ? (PlatformEventTypes.ExecutionRunCompleted, PlatformEventSeverities.Info)
            : (PlatformEventTypes.ExecutionRunFailed, PlatformEventSeverities.Error);

    private static string BuildDataJson(ExecutionLifecycleTerminalContext context)
    {
        var data = new Dictionary<string, object?>
        {
            [PlatformEventDataProperties.ExecutionKind] = context.ExecutionKind,
            [PlatformEventDataProperties.DefinitionId] = context.DefinitionId,
            [PlatformEventDataProperties.Status] = context.TerminalStatus,
        };

        if (context.StartedAtUtc is { } started && context.CompletedAtUtc > started)
        {
            var ms = (long)(context.CompletedAtUtc - started).TotalMilliseconds;
            if (ms >= 0)
                data[PlatformEventDataProperties.DurationMs] = ms;
        }

        if (context.StepCount is { } steps)
            data[PlatformEventDataProperties.StepCount] = steps;

        if (string.Equals(context.TerminalStatus, WorkflowLifecycleStatusNames.Completed, StringComparison.Ordinal))
        {
            data[PlatformEventDataProperties.Outcome] = "success";
            return JsonSerializer.Serialize(data, JsonOptions);
        }

        data[PlatformEventDataProperties.FailureCode] = context.FailureCode ?? "execution_failed";
        data[PlatformEventDataProperties.FailureMessageExcerpt] = TruncateExcerpt(context.FailureSummary);

        if (!string.IsNullOrWhiteSpace(context.FailedStepName))
            data[PlatformEventDataProperties.FailedStepName] = context.FailedStepName;

        if (!string.IsNullOrWhiteSpace(context.ExceptionType))
            data[PlatformEventDataProperties.ExceptionType] = context.ExceptionType;

        return JsonSerializer.Serialize(data, JsonOptions);
    }

    private static string TruncateExcerpt(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Execution failed.";
        var trimmed = message.Trim();
        return trimmed.Length <= FailureExcerptMaxLength
            ? trimmed
            : trimmed[..FailureExcerptMaxLength];
    }
}
