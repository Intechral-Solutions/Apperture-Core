namespace Apperture.RuntimePlane.Abstractions;

/// <summary>Optional correlation identifiers propagated into step execution / module platform signals (Epic 77 Phase 2).</summary>
/// <param name="ResourceContextId">
/// CTX-2: immutable ResourceContext snapshot read straight off the already-loaded OperationRun row.
/// Never re-derived from current Integration/Schedule ownership during execution.
/// </param>
public sealed record WorkflowStepExecutionScope(
    string? RunId = null,
    string? ProcedureId = null,
    string? ScheduleId = null,
    string? CorrelationId = null,
    string? CausationId = null,
    string? ResourceContextId = null);
