namespace Apperture.Core.Application;

/// <summary>
/// Audit action names for lifecycle-gated execution outcomes (Epic 27).
/// </summary>
public static class LifecycleExecutionAuditActionNames
{
    public const string WorkflowStartBlocked = "execution.workflow_start.blocked";

    public const string OperationStartBlocked = "execution.operation_start.blocked";

    public const string WorkflowRunDefinitionUnavailable = "execution.workflow_run.failed_definition_unavailable";

    public const string OperationRunDefinitionUnavailable = "execution.operation_run.failed_definition_unavailable";

    public const string RuntimeCompositionInvalidAtStartup = "runtime.composition.invalid_at_startup";

    /// <summary>
    /// Workflow retry blocked because the definition is not executable (e.g. module disabled).
    /// Epic 27 corrective pass.
    /// </summary>
    public const string WorkflowRetryBlocked = "execution.workflow_retry.blocked";
}
