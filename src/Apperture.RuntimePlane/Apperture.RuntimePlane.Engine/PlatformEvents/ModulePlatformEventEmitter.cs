using System.Text.Json;
using System.Text.Json.Serialization;
using Apperture.Core.Application.PlatformEvents;
using Apperture.ModuleSdk.Abstractions.Signals;
using Microsoft.Extensions.Options;

namespace Apperture.RuntimePlane.Engine.PlatformEvents;

/// <summary>Worker-hosted module-authored platform events delegating into Phase 1 <see cref="IPlatformEventEmitter"/>.</summary>
public sealed class ModulePlatformEventEmitter : IModulePlatformEventEmitter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IModuleSignalRegistry _registry;
    private readonly IPlatformEventEmitter _emitter;
    private readonly IOptions<PlatformEventsOptions> _options;
    private readonly TimeProvider _time;

    public ModulePlatformEventEmitter(
        IModuleSignalRegistry registry,
        IPlatformEventEmitter emitter,
        IOptions<PlatformEventsOptions> options,
        TimeProvider time)
    {
        _registry = registry;
        _emitter = emitter;
        _options = options;
        _time = time;
    }

    /// <inheritdoc />
    public async ValueTask<ModulePlatformEventEmitResult> EmitAsync(
        IModulePlatformEmitContext context,
        string signalName,
        object? payload,
        string? severityOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Enabled)
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
        {
            dataJson = "{}";
        }
        else
        {
            dataJson = JsonSerializer.Serialize(payload, payload.GetType(), JsonOptions);
        }

        var runSegment = string.IsNullOrWhiteSpace(context.RunId) ? "" : ":" + context.RunId;
        var subject = $"{context.ModuleId}:{registration.SignalName}{runSegment}";

        var request = new PlatformEventEmitRequest(
            registration.Descriptor.Type,
            registration.Descriptor.DefaultSource,
            dataJson,
            _time.GetUtcNow(),
            severity!,
            Subject: subject,
            IntegrationId: context.IntegrationId,
            ModuleId: context.ModuleId,
            ProcedureId: context.ProcedureId,
            RunId: context.RunId,
            ScheduleId: context.ScheduleId,
            CorrelationId: context.CorrelationId,
            CausationId: context.CausationId,
            ResourceContextId: context.ResourceContextId);

        var envelope = await _emitter.EmitAsync(request, cancellationToken).ConfigureAwait(false);
        return new ModulePlatformEventEmitResult(
            Emitted: true,
            SkippedBecauseDisabled: false,
            EventId: envelope.Id.Value);
    }
}
