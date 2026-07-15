namespace Apperture.ModuleSdk.Testing;

/// <summary>Correlation and runtime options for <see cref="ModuleTestHarness"/>.</summary>
public sealed class ModuleTestHarnessOptions
{
    public string? IntegrationId { get; set; }

    public string? RunId { get; set; }

    public string? ProcedureId { get; set; }

    public string? ScheduleId { get; set; }

    public string? CorrelationId { get; set; }

    public string? CausationId { get; set; }

    public bool PlatformEventsEnabled { get; set; } = true;

    public Func<string, object?>? SlotResolver { get; set; }

    /// <summary>Optional in-memory/durable store for SDK-3A operation itemization tests.</summary>
    public Apperture.Core.Application.OperationItems.IOperationItemStore? OperationItemStore { get; set; }
}
