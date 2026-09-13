using System.Text.Json;
using System.Text.Json.Serialization;
using Apperture.Core.Application.PlatformEvents;
using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.ModuleSdk.Testing.Signals;

/// <summary>
/// In-memory <see cref="IModulePlatformEventEmitter"/> with manifest validation aligned to runtime (Epic 103C).
/// </summary>
public sealed class RecordingModulePlatformEventEmitter : IModulePlatformEventEmitter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IModuleSignalRegistry _registry;
    private readonly bool _enabled;
    private readonly TimeProvider _time;
    private readonly List<RecordedPlatformEvent> _events = [];

    public RecordingModulePlatformEventEmitter(
        IModuleSignalRegistry registry,
        bool enabled = true,
        TimeProvider? time = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _enabled = enabled;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Successfully recorded emissions (not skipped).</summary>
    public IReadOnlyList<RecordedPlatformEvent> EmittedEvents => _events;

    /// <inheritdoc />
    public async ValueTask<ModulePlatformEventEmitResult> EmitAsync(
        IModulePlatformEmitContext context,
        string signalName,
        object? payload,
        string? severityOverride = null,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        if (!_enabled)
        {
            return new ModulePlatformEventEmitResult(
                Emitted: false,
                SkippedBecauseDisabled: true,
                SkipReason: "PlatformEvents:Enabled=false.");
        }

        if (!_registry.TryResolve(context.ModuleId, signalName, out var registration) || registration is null)
        {
            throw new PlatformEventValidationException(
                $"Undeclared module signal '{signalName}' for module '{context.ModuleId}'.");
        }

        var severity = string.IsNullOrWhiteSpace(severityOverride)
            ? registration.Declaration.DefaultSeverity
            : severityOverride;

        string dataJson;
        if (payload is null)
            dataJson = "{}";
        else
            dataJson = JsonSerializer.Serialize(payload, payload.GetType(), JsonOptions);

        var runSegment = string.IsNullOrWhiteSpace(context.RunId) ? "" : ":" + context.RunId;
        var subject = $"{context.ModuleId}:{registration.SignalName}{runSegment}";
        var occurredAt = _time.GetUtcNow();

        var request = new PlatformEventEmitRequest(
            registration.Descriptor.Type,
            registration.Descriptor.DefaultSource,
            dataJson,
            occurredAt,
            severity!,
            Subject: subject,
            IntegrationId: context.IntegrationId,
            ModuleId: context.ModuleId,
            ProcedureId: context.ProcedureId,
            RunId: context.RunId,
            ScheduleId: context.ScheduleId,
            CorrelationId: context.CorrelationId,
            CausationId: context.CausationId);

        PlatformEventCatalog.ValidateEmitRequest(request, registration.Descriptor);

        var eventId = $"pevt_test_{Guid.NewGuid():N}";
        _events.Add(new RecordedPlatformEvent(
            registration.SignalName,
            request.Type,
            request.Source,
            request.Severity,
            request.DataJson,
            subject,
            context.ModuleId,
            context.IntegrationId,
            context.RunId,
            context.ProcedureId,
            context.ScheduleId,
            context.CorrelationId,
            context.CausationId,
            occurredAt,
            eventId));

        return await ValueTask.FromResult(
            new ModulePlatformEventEmitResult(
                Emitted: true,
                SkippedBecauseDisabled: false,
                EventId: eventId));
    }

    /// <summary>Throws when no emission exists for <paramref name="signalName"/>.</summary>
    public RecordedPlatformEvent GetEvent(string signalName)
    {
        var match = _events.LastOrDefault(e =>
            string.Equals(e.SignalName, signalName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            throw new InvalidOperationException($"No recorded platform event for signal '{signalName}'.");
        return match;
    }

    /// <summary>Asserts at least one emission for <paramref name="signalName"/>.</summary>
    public void ShouldHaveEmitted(string signalName)
    {
        if (!_events.Any(e => string.Equals(e.SignalName, signalName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Expected platform event '{signalName}' was not emitted.");
    }

    /// <summary>Alias for <see cref="ShouldHaveEmitted"/>.</summary>
    public void ShouldContain(string signalName) => ShouldHaveEmitted(signalName);
}
