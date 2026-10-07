using Apperture.Core.Application.Audit;

namespace Apperture.Core.Application;

/// <summary>
/// Audit action names for lifecycle-gated execution outcomes (Epic 27). The start and retry refusals are written through the
/// audit recorder (AUD-1.3), so their spelling is <see cref="AuditActionNames"/>'s.
/// </summary>
public static class LifecycleExecutionAuditActionNames
{
    public const string WorkflowStartBlocked = AuditActionNames.WorkflowStartBlocked;

    public const string OperationStartBlocked = AuditActionNames.OperationStartBlocked;

    public const string WorkflowRunDefinitionUnavailable = "execution.workflow_run.failed_definition_unavailable";

    public const string OperationRunDefinitionUnavailable = "execution.operation_run.failed_definition_unavailable";

    public const string RuntimeCompositionInvalidAtStartup = "runtime.composition.invalid_at_startup";

    /// <summary>
    /// Workflow retry blocked because the definition is not executable (e.g. module disabled).
    /// Epic 27 corrective pass.
    /// </summary>
    public const string WorkflowRetryBlocked = AuditActionNames.WorkflowRetryBlocked;
}
