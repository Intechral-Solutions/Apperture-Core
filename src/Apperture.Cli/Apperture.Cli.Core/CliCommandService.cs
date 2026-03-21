using Apperture.Management.Client;

namespace Apperture.Cli.Core;

public sealed class CliCommandService
{
    private readonly IManagementClient _client;

    public CliCommandService(IManagementClient client) => _client = client;

    public Task<IReadOnlyList<ModuleDto>> GetModulesAsync(CancellationToken cancellationToken = default) =>
        _client.GetModulesAsync(cancellationToken);

    public Task<IReadOnlyList<WorkflowDto>> GetWorkflowsAsync(string moduleId, CancellationToken cancellationToken = default) =>
        _client.GetWorkflowsAsync(moduleId, cancellationToken);

    public Task<StartWorkflowResult> StartWorkflowAsync(string workflowId, IReadOnlyDictionary<string, object?>? parameters, string? idempotencyKey, CancellationToken cancellationToken = default) =>
        _client.StartWorkflowAsync(workflowId, parameters, idempotencyKey, correlationId: null, cancellationToken);

    public async Task<IReadOnlyList<RunDto>> GetRunsAsync(CancellationToken cancellationToken = default)
    {
        var list = await _client.GetRunsAsync(cancellationToken).ConfigureAwait(false);
        return list.Select(r => new RunDto(r.RunId, r.ExecutionInstanceId, r.Status, r.CreatedAt)).ToList();
    }

    public async Task<RunDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var r = await _client.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);
        return r is null ? null : new RunDto(r.RunId, r.ExecutionInstanceId, r.Status, r.CreatedAt);
    }
}
