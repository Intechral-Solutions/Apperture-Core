namespace Apperture.ControlPlane.Security;

/// <summary>
/// Stable machine codes for why a procedure is not launchable on an integration (list/detail projection).
/// </summary>
public static class ProcedureBlockedReasonCodes
{
    public const string IntegrationNotReady = "integration_not_ready";

    public const string TargetNotExecutable = "target_not_executable";

    public const string InsufficientProcedurePermission = "insufficient_procedure_permission";

    /// <summary>Actor cannot start the target workflow (e.g. missing workflow.execute for module scope).</summary>
    public const string InsufficientWorkflowPermission = "insufficient_workflow_permission";

    /// <summary>Actor cannot start the target operation (e.g. missing operation.execute).</summary>
    public const string InsufficientOperationPermission = "insufficient_operation_permission";
}

/// <summary>
/// Launch failures for procedure HTTP mapping (Epic 56A).
/// </summary>
public enum IntegrationProcedureLaunchErrorCode
{
    IntegrationNotFound,
    IntegrationReadinessUnavailable,
    IntegrationNotReady,
    ProcedureNotFound,
    ProcedureWrongModule,
    TargetNotExecutable,
    ProcedurePermissionDenied,
    InvalidParameters,
    UnsafeOrchestrationParameters,
    WorkflowStartForbidden,
    OperationStartForbidden,
    DispatchFailed
}

/// <summary>
/// Result of <see cref="IIntegrationProcedureService.LaunchAsync"/> — discriminated success vs failure for truthful HTTP semantics.
/// </summary>
public abstract record IntegrationProcedureLaunchOutcome
{
    private IntegrationProcedureLaunchOutcome()
    {
    }

    public sealed record Succeeded(IntegrationProcedureLaunchResponse Response) : IntegrationProcedureLaunchOutcome;

    public sealed record Failed(IntegrationProcedureLaunchErrorCode Code, string Message) : IntegrationProcedureLaunchOutcome;
}

/// <summary>
/// Maps launch error codes to API <c>code</c> strings and HTTP status (controller).
/// </summary>
public static class IntegrationProcedureLaunchErrorCodeExtensions
{
    public static string ToApiCode(this IntegrationProcedureLaunchErrorCode code) =>
        code switch
        {
            IntegrationProcedureLaunchErrorCode.IntegrationNotFound => "integration_not_found",
            IntegrationProcedureLaunchErrorCode.IntegrationReadinessUnavailable => "integration_readiness_unavailable",
            IntegrationProcedureLaunchErrorCode.IntegrationNotReady => "integration_not_ready",
            IntegrationProcedureLaunchErrorCode.ProcedureNotFound => "procedure_not_found",
            IntegrationProcedureLaunchErrorCode.ProcedureWrongModule => "procedure_wrong_module",
            IntegrationProcedureLaunchErrorCode.TargetNotExecutable => "target_not_executable",
            IntegrationProcedureLaunchErrorCode.ProcedurePermissionDenied => "procedure_permission_denied",
            IntegrationProcedureLaunchErrorCode.InvalidParameters => "invalid_parameters",
            IntegrationProcedureLaunchErrorCode.UnsafeOrchestrationParameters => "unsafe_orchestration_parameters",
            IntegrationProcedureLaunchErrorCode.WorkflowStartForbidden => "workflow_start_forbidden",
            IntegrationProcedureLaunchErrorCode.OperationStartForbidden => "operation_start_forbidden",
            IntegrationProcedureLaunchErrorCode.DispatchFailed => "dispatch_failed",
            _ => "unknown"
        };

    public static int ToHttpStatus(this IntegrationProcedureLaunchErrorCode code) =>
        code switch
        {
            IntegrationProcedureLaunchErrorCode.IntegrationNotFound or
                IntegrationProcedureLaunchErrorCode.ProcedureNotFound or
                IntegrationProcedureLaunchErrorCode.ProcedureWrongModule or
                IntegrationProcedureLaunchErrorCode.TargetNotExecutable or
                IntegrationProcedureLaunchErrorCode.IntegrationReadinessUnavailable => 404,
            IntegrationProcedureLaunchErrorCode.IntegrationNotReady => 409,
            IntegrationProcedureLaunchErrorCode.InvalidParameters or
                IntegrationProcedureLaunchErrorCode.UnsafeOrchestrationParameters => 400,
            IntegrationProcedureLaunchErrorCode.ProcedurePermissionDenied or
                IntegrationProcedureLaunchErrorCode.WorkflowStartForbidden or
                IntegrationProcedureLaunchErrorCode.OperationStartForbidden => 403,
            IntegrationProcedureLaunchErrorCode.DispatchFailed => 500,
            _ => 500
        };
}
