using Apperture.RuntimePlane.Abstractions;

namespace Apperture.Management.Client;

public interface IManagementClient
{
    Task<IReadOnlyList<ModuleDto>> GetModulesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowDto>> GetWorkflowsAsync(string moduleId, CancellationToken cancellationToken = default);
    Task<OperationRunId> StartWorkflowAsync(string workflowId, IReadOnlyDictionary<string, object?>? parameters, string? idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RunDto>> GetRunsAsync(CancellationToken cancellationToken = default);
    Task<RunDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default);
}
