using Apperture.RuntimePlane.Abstractions;
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

    public async Task<StartWorkflowResult> StartWorkflowAsync(
        string workflowId,
        IReadOnlyDictionary<string, object?>? parameters,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"workflows/{workflowId}/start",
            new { parameters, idempotencyKey, correlationId },
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<StartWorkflowResult>(cancellationToken).ConfigureAwait(false);
        return result ?? new StartWorkflowResult(Guid.NewGuid().ToString("N"), "unknown", workflowId, "requested");
    }

    public async Task<IReadOnlyList<RunSummaryDto>> GetRunsAsync(CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<RunSummaryDto[]>("runs", cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<RunSummaryDto>();
    }

    public async Task<RunDetailDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<RunDetailDto>($"runs/{runId}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<CancelRunResult> CancelRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"runs/{runId}/cancel", content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CancelRunResult>(cancellationToken).ConfigureAwait(false);
        return result ?? new CancelRunResult(runId, "cancel_requested");
    }

    public async Task<RetryRunResult> RetryRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"runs/{runId}/retry", content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RetryRunResult>(cancellationToken).ConfigureAwait(false);
        return result ?? new RetryRunResult(runId, Guid.NewGuid().ToString("N"), "unknown", "unknown", "requested", runId);
    }

    public async Task<ExternalControlResult> ParkRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"runs/{runId}/park", content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ExternalControlResult>(cancellationToken).ConfigureAwait(false);
        return result ?? new ExternalControlResult(runId, "unknown", "unknown", WorkflowExternalControlKindNames.Park, "queued", "running", null);
    }

    public async Task<ExternalControlResult> ResumeRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"runs/{runId}/resume", content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ExternalControlResult>(cancellationToken).ConfigureAwait(false);
        return result ?? new ExternalControlResult(runId, "unknown", "unknown", WorkflowExternalControlKindNames.Resume, "queued", "parked", null);
    }
}
