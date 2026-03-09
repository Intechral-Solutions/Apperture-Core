using Apperture.Management.Client;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.Cli.Core;

public sealed class CliCommandService
{
    private readonly IManagementClient _client;

    public CliCommandService(IManagementClient client) => _client = client;

    public Task<IReadOnlyList<ModuleDto>> GetModulesAsync(CancellationToken cancellationToken = default) =>
        _client.GetModulesAsync(cancellationToken);

    public Task<IReadOnlyList<WorkflowDto>> GetWorkflowsAsync(string moduleId, CancellationToken cancellationToken = default) =>
        _client.GetWorkflowsAsync(moduleId, cancellationToken);

    public Task<OperationRunId> StartWorkflowAsync(string workflowId, IReadOnlyDictionary<string, object?>? parameters, string? idempotencyKey, CancellationToken cancellationToken = default) =>
        _client.StartWorkflowAsync(workflowId, parameters, idempotencyKey, cancellationToken);

    public Task<IReadOnlyList<RunDto>> GetRunsAsync(CancellationToken cancellationToken = default) =>
        _client.GetRunsAsync(cancellationToken);

    public Task<RunDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default) =>
        _client.GetRunAsync(runId, cancellationToken);
}
