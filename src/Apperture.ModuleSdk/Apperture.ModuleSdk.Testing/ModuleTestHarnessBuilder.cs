using Apperture.ModuleSdk.Abstractions;

namespace Apperture.ModuleSdk.Testing;

/// <summary>Fluent builder for <see cref="ModuleTestHarness"/> (Epic 103C).</summary>
public sealed class ModuleTestHarnessBuilder
{
    private readonly IModule _module;
    private readonly ModuleTestHarnessOptions _options = new();

    internal ModuleTestHarnessBuilder(IModule module) =>
        _module = module ?? throw new ArgumentNullException(nameof(module));

    public ModuleTestHarnessBuilder WithIntegrationId(string integrationId)
    {
        _options.IntegrationId = integrationId;
        return this;
    }

    public ModuleTestHarnessBuilder WithRunId(string runId)
    {
        _options.RunId = runId;
        return this;
    }

    public ModuleTestHarnessBuilder WithProcedureId(string procedureId)
    {
        _options.ProcedureId = procedureId;
        return this;
    }

    public ModuleTestHarnessBuilder WithScheduleId(string scheduleId)
    {
        _options.ScheduleId = scheduleId;
        return this;
    }

    public ModuleTestHarnessBuilder WithCorrelationId(string correlationId)
    {
        _options.CorrelationId = correlationId;
        return this;
    }

    public ModuleTestHarnessBuilder WithCausationId(string causationId)
    {
        _options.CausationId = causationId;
        return this;
    }

    public ModuleTestHarnessBuilder WithPlatformEventsEnabled(bool enabled)
    {
        _options.PlatformEventsEnabled = enabled;
        return this;
    }

    /// <summary>Resolves slot names to test doubles (e.g. <c>Db</c> → stub <c>IDbConnection</c>).</summary>
    public ModuleTestHarnessBuilder WithSlotResolver(Func<string, object?> slotResolver)
    {
        _options.SlotResolver = slotResolver;
        return this;
    }

    public ModuleTestHarness Build() => ModuleTestHarness.Create(_module, _options);
}
