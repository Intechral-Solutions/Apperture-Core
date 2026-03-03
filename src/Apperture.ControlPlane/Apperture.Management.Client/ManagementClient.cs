using Apperture.Shared.Kernel;
using System.Net.Http.Json;

namespace Apperture.Management.Client;

public sealed class ManagementClient : IManagementClient
{
    private readonly HttpClient _http;

    public ManagementClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<ModuleDto>> GetModulesAsync(CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<ModuleDto[]>("modules", cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<ModuleDto>();
    }

    public async Task<IReadOnlyList<WorkflowDto>> GetWorkflowsAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<WorkflowDto[]>($"modules/{moduleId}/workflows", cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<WorkflowDto>();
    }

    public async Task<OperationRunId> StartWorkflowAsync(string workflowId, IReadOnlyDictionary<string, object?>? parameters, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync($"workflows/{workflowId}/start", new { parameters, idempotencyKey }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<StartResponse>(cancellationToken).ConfigureAwait(false);
        return new OperationRunId(result?.Id ?? Guid.NewGuid().ToString("N"));
    }

    public async Task<IReadOnlyList<RunDto>> GetRunsAsync(CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<RunDto[]>("runs", cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<RunDto>();
    }

    public async Task<RunDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<RunDto>($"runs/{runId}", cancellationToken).ConfigureAwait(false);
    }

    private sealed record StartResponse(string Id);
}
