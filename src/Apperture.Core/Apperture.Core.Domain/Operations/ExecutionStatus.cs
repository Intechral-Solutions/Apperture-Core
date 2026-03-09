namespace Apperture.Core.Domain.Operations;

/// <summary>
/// Status of an execution run.
/// </summary>
public enum ExecutionStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}
