namespace Apperture.Shared.Kernel;

/// <summary>
/// Execution context passed into operations: actor, environment, correlation, and clock. No workflow/run details.
/// </summary>
public interface IExecutionContext
{
    IActor Actor { get; }
    IEnvironmentContext Environment { get; }
    CorrelationId CorrelationId { get; }
    IAppertureClock Clock { get; }
}
