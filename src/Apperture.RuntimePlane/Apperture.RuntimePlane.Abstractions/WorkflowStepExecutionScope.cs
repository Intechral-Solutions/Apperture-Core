namespace Apperture.RuntimePlane.Abstractions;

/// <summary>Optional correlation identifiers propagated into step execution / module platform signals (Epic 77 Phase 2).</summary>
public sealed record WorkflowStepExecutionScope(
    string? RunId = null,
    string? ProcedureId = null,
    string? ScheduleId = null,
    string? CorrelationId = null,
    string? CausationId = null);
