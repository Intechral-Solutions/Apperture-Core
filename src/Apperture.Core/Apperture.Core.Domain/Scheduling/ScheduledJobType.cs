namespace Apperture.Core.Domain.Scheduling;

/// <summary>
/// Type of scheduled job target.
/// </summary>
public enum ScheduledJobType
{
    WorkflowExecution,
    ModuleOperationExecution,
    ConnectorSyncOrPolling,
    PowerShellScriptExecution,
    InstanceMaintenanceTask,
    Custom
}
