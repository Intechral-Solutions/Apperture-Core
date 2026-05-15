namespace Apperture.ModuleSdk.Abstractions.Signals;

/// <summary>
/// Correlation and tenancy fields surfaced to modules for platform event emission (Epic 77).
/// Step execution contexts in the runtime implement this contract.
/// </summary>
public interface IModulePlatformEmitContext
{
    string ModuleId { get; }

    string? IntegrationId { get; }

    string? RunId { get; }

    string? ProcedureId { get; }

    string? ScheduleId { get; }

    string? CorrelationId { get; }

    string? CausationId { get; }
}
