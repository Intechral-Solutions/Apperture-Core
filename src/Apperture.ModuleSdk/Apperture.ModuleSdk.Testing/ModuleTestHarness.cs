using Apperture.Core.Application.Execution;
using Apperture.Core.Application.PlatformEvents;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.ModuleSdk.Testing.Signals;

namespace Apperture.ModuleSdk.Testing;

/// <summary>
/// Author-facing harness for module signal behavior: platform events, operational logs, telemetry (Epic 103C).
/// </summary>
public sealed class ModuleTestHarness
{
    private readonly ModuleTestHarnessOptions _options;

    private ModuleTestHarness(
        IModule module,
        ModuleManifest manifest,
        IModuleSignalRegistry registry,
        RecordingModulePlatformEventEmitter platformEvents,
        RecordingModuleTelemetry telemetry,
        ModuleTestHarnessOptions options)
    {
        Module = module;
        Manifest = manifest;
        Registry = registry;
        PlatformEvents = platformEvents;
        Telemetry = telemetry;
        _options = options;
    }

    public IModule Module { get; }

    public ModuleManifest Manifest { get; }

    public IModuleSignalRegistry Registry { get; }

    /// <summary>Recording platform event emitter (alias for <see cref="PlatformEvents"/>).</summary>
    public RecordingModulePlatformEventEmitter PlatformEvents { get; }

    public RecordingModuleTelemetry Telemetry { get; }

    public static ModuleTestHarnessBuilder ForModule(IModule module) => new(module);

    public static ModuleManifest GetManifest(IModule module) => module.GetManifest();

    internal static ModuleTestHarness Create(IModule module, ModuleTestHarnessOptions options)
    {
        ModuleSignalManifestValidator.ThrowIfInvalid([module]);
        var manifest = module.GetManifest();
        var registry = ModuleSignalRegistry.FromModules([module]);
        var platformEvents = new RecordingModulePlatformEventEmitter(registry, options.PlatformEventsEnabled);
        var telemetry = new RecordingModuleTelemetry();
        return new ModuleTestHarness(module, manifest, registry, platformEvents, telemetry, options);
    }

    /// <summary>Re-validates manifest signals (e.g. after mutating test doubles).</summary>
    public void AssertManifestValid() => ModuleSignalManifestValidator.ThrowIfInvalid([Module]);

    /// <summary>Builds a step context wired to harness recorders and correlation options.</summary>
    public HarnessStepExecutionContext CreateStepContext(string stepName)
    {
        var logs = new InMemoryOperationalLogSink(
            Manifest.Id,
            _options.IntegrationId,
            _options.RunId,
            _options.ScheduleId,
            _options.ProcedureId,
            _options.CorrelationId,
            _options.CausationId,
            stepName);

        return new HarnessStepExecutionContext(
            Manifest.Id,
            PlatformEvents,
            logs,
            Telemetry,
            _options.IntegrationId,
            _options.RunId,
            _options.ProcedureId,
            _options.ScheduleId,
            _options.CorrelationId,
            _options.CausationId,
            _options.SlotResolver);
    }

    /// <summary>Invokes a step handler with <see cref="CreateStepContext"/>.</summary>
    public async ValueTask<object?> ExecuteStepAsync(
        IStepHandler handler,
        string stepName,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var context = CreateStepContext(stepName);
        return await handler.ExecuteAsync(context, parameters ?? new Dictionary<string, object?>(), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Gets the operational log sink from a context created for <paramref name="stepName"/>.</summary>
    public static InMemoryOperationalLogSink GetOperationalLogs(IStepExecutionContext context) =>
        (InMemoryOperationalLogSink)context.Signals.Logs;
}
