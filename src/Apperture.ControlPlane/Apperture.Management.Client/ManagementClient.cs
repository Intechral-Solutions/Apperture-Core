using System.Net.Http.Json;
using System.Text.Json;
using Apperture.RuntimePlane.Abstractions;

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

    public async Task<ModuleDto?> GetModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<ModuleDto>($"modules/{moduleId}", cancellationToken).ConfigureAwait(false);
    }

    public Task<ModuleLifecycleActionResultDto> InstallModuleAsync(string moduleId, CancellationToken cancellationToken = default)
        => PostModuleLifecycleActionAsync($"modules/{moduleId}/install", moduleId, "install", cancellationToken);

    public Task<ModuleLifecycleActionResultDto> EnableModuleAsync(string moduleId, CancellationToken cancellationToken = default)
        => PostModuleLifecycleActionAsync($"modules/{moduleId}/enable", moduleId, "enable", cancellationToken);

    public Task<ModuleLifecycleActionResultDto> DisableModuleAsync(string moduleId, CancellationToken cancellationToken = default)
        => PostModuleLifecycleActionAsync($"modules/{moduleId}/disable", moduleId, "disable", cancellationToken);

    public Task<ModuleLifecycleActionResultDto> UninstallModuleAsync(string moduleId, CancellationToken cancellationToken = default)
        => PostModuleLifecycleActionAsync($"modules/{moduleId}/uninstall", moduleId, "uninstall", cancellationToken);

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
        return await GetRunsAsync(filter: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RunSummaryDto>> GetRunsAsync(RunQueryFilter? filter, CancellationToken cancellationToken = default)
    {
        var query = BuildRunsQuery(filter);
        var list = await _http.GetFromJsonAsync<RunSummaryDto[]>($"runs{query}", cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<RunSummaryDto>();
    }

    public async Task<RunDetailDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<RunDetailDto>($"runs/{runId}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ControlActionAvailabilityDto>> GetRunActionsAsync(string runId, CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<ControlActionAvailabilityDto[]>($"runs/{runId}/actions", cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<ControlActionAvailabilityDto>();
    }

    public async Task<RunTimelineDto?> GetRunTimelineAsync(string runId, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<RunTimelineDto>($"runs/{runId}/timeline", cancellationToken).ConfigureAwait(false);
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

    public async Task<IReadOnlyList<OperationDefinitionDto>> GetOperationsAsync(CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<OperationDefinitionDto[]>("operations", cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<OperationDefinitionDto>();
    }

    public async Task<OperationDefinitionDto?> GetOperationAsync(string operationDefinitionId, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<OperationDefinitionDto>($"operations/{operationDefinitionId}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlCenterDto> GetControlCenterAsync(CancellationToken cancellationToken = default)
    {
        var dto = await _http.GetFromJsonAsync<ControlCenterDto>("control/center", cancellationToken).ConfigureAwait(false);
        return dto ?? new ControlCenterDto([], [], []);
    }

    public async Task<HostEffectiveModuleSnapshotDto> GetRuntimeCompositionAsync(CancellationToken cancellationToken = default)
    {
        var dto = await _http.GetFromJsonAsync<HostEffectiveModuleSnapshotDto>(
            "control/runtime-composition",
            cancellationToken).ConfigureAwait(false);
        return dto ?? new HostEffectiveModuleSnapshotDto(
            DateTimeOffset.UtcNow,
            [],
            [],
            [],
            [],
            []);
    }

    public async Task<IReadOnlyList<RuntimeControlRequestDto>> GetRuntimeShutdownRequestsAsync(
        string runtimeId,
        CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<RuntimeControlRequestDto[]>(
            $"runtime-instances/{runtimeId}/controls/shutdown/requests",
            cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<RuntimeControlRequestDto>();
    }

    public async Task<AuditTimelinePageDto> GetAuditTimelineAsync(
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? actor = null,
        string? actionPrefix = null,
        string? moduleId = null,
        string? scheduleId = null,
        string? runId = null,
        string? outcome = null,
        int pageSize = 50,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var pairs = new List<string>();
        if (fromUtc is not null)
            pairs.Add($"fromUtc={Uri.EscapeDataString(fromUtc.Value.ToString("o"))}");
        if (toUtc is not null)
            pairs.Add($"toUtc={Uri.EscapeDataString(toUtc.Value.ToString("o"))}");
        Add(pairs, "actor", actor);
        Add(pairs, "actionPrefix", actionPrefix);
        Add(pairs, "moduleId", moduleId);
        Add(pairs, "scheduleId", scheduleId);
        Add(pairs, "runId", runId);
        Add(pairs, "outcome", outcome);
        pairs.Add($"pageSize={pageSize}");
        Add(pairs, "continuationToken", continuationToken);

        var query = pairs.Count > 0 ? "?" + string.Join("&", pairs) : "";
        var dto = await _http.GetFromJsonAsync<AuditTimelinePageDto>(
            $"control/audit{query}",
            cancellationToken).ConfigureAwait(false);
        return dto ?? new AuditTimelinePageDto([], null);
    }

    public async Task<AuditTimelineEntryDto?> GetAuditEntryAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<AuditTimelineEntryDto>(
            $"control/audit/{Uri.EscapeDataString(id)}",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<RuntimeHealthSnapshotDto?> GetRuntimeHealthAsync(
        string? runtimePlaneId = "local",
        int blockedDeferredWindowHours = 24,
        int scheduleFailureWindowHours = 24,
        CancellationToken cancellationToken = default)
    {
        var query = $"?runtimePlaneId={Uri.EscapeDataString(runtimePlaneId ?? "local")}&blockedDeferredWindowHours={blockedDeferredWindowHours}&scheduleFailureWindowHours={scheduleFailureWindowHours}";
        return await _http.GetFromJsonAsync<RuntimeHealthSnapshotDto>(
            $"control/runtime-health{query}",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScheduleDto>> GetSchedulesAsync(CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<ScheduleDto[]>("schedules", cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<ScheduleDto>();
    }

    public async Task<ScheduleDetailDto?> GetScheduleAsync(string id, string? runtimePlaneId = "local", CancellationToken cancellationToken = default)
    {
        var query = string.IsNullOrEmpty(runtimePlaneId) ? "" : $"?runtimePlaneId={Uri.EscapeDataString(runtimePlaneId)}";
        return await _http.GetFromJsonAsync<ScheduleDetailDto>(
            $"schedules/{Uri.EscapeDataString(id)}{query}",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task EnableScheduleAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"schedules/{Uri.EscapeDataString(id)}/enable", content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task DisableScheduleAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"schedules/{Uri.EscapeDataString(id)}/disable", content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<BlockedWorkRemediationEntryDto>> GetBlockedWorkAsync(
        string? runtimePlaneId = "local",
        int windowHours = 24,
        CancellationToken cancellationToken = default)
    {
        var query = $"?runtimePlaneId={Uri.EscapeDataString(runtimePlaneId ?? "local")}&windowHours={windowHours}";
        var list = await _http.GetFromJsonAsync<BlockedWorkRemediationEntryDto[]>(
            $"control/recovery/blocked-work{query}",
            cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<BlockedWorkRemediationEntryDto>();
    }

    public async Task<BlockedWorkRemediationEntryDto?> GetBlockedWorkItemAsync(string runId, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<BlockedWorkRemediationEntryDto>(
            $"control/recovery/blocked-work/{Uri.EscapeDataString(runId)}",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<BlockedWorkRetryResult> RetryBlockedWorkAsync(string runId, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync(
            $"control/recovery/blocked-work/{Uri.EscapeDataString(runId)}/retry",
            content: null,
            cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
            var newRunId = json.TryGetProperty("runId", out var r) ? r.GetString() : null;
            return new BlockedWorkRetryResult(true, newRunId, null, false);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
            var inspectionOnly = json.TryGetProperty("inspectionOnly", out var io) && io.GetBoolean();
            var error = json.TryGetProperty("message", out var m) ? m.GetString() : json.TryGetProperty("error", out var e) ? e.GetString() : response.ReasonPhrase;
            return new BlockedWorkRetryResult(false, null, error, inspectionOnly);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
            var error = json.TryGetProperty("error", out var e) ? e.GetString() : response.ReasonPhrase;
            return new BlockedWorkRetryResult(false, null, error ?? "Conflict", false);
        }

        response.EnsureSuccessStatusCode();
        return new BlockedWorkRetryResult(false, null, response.ReasonPhrase, false);
    }

    public async Task TriggerSchedulerRunCycleAsync(string? runtimePlaneId = "local", CancellationToken cancellationToken = default)
    {
        var query = $"?runtimePlaneId={Uri.EscapeDataString(runtimePlaneId ?? "local")}";
        var response = await _http.PostAsync($"control/recovery/schedules/run-cycle{query}", content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<OperatorCapabilitiesDto> GetOperatorCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var dto = await _http.GetFromJsonAsync<OperatorCapabilitiesDto>("control/capabilities", cancellationToken).ConfigureAwait(false);
        return dto ?? new OperatorCapabilitiesDto(false, false, false, false, false);
    }

    public async Task<StartOperationResult> StartOperationAsync(
        string operationDefinitionId,
        IReadOnlyDictionary<string, object?>? parameters,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"operations/{operationDefinitionId}/start",
            new { parameters, idempotencyKey, correlationId },
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<StartOperationResult>(cancellationToken).ConfigureAwait(false);
        return result ?? new StartOperationResult(Guid.NewGuid().ToString("N"), "unknown", operationDefinitionId, "requested");
    }

    private static string BuildRunsQuery(RunQueryFilter? filter)
    {
        if (filter is null)
            return string.Empty;

        var pairs = new List<string>();
        Add(pairs, "executionKind", filter.ExecutionKind);
        Add(pairs, "operationDefinitionId", filter.OperationDefinitionId);
        Add(pairs, "status", filter.Status);
        Add(pairs, "moduleId", filter.ModuleId);
        Add(pairs, "correlationKey", filter.CorrelationKey);
        if (filter.CreatedFrom is not null)
            Add(pairs, "createdFrom", filter.CreatedFrom.Value.ToString("o"));
        if (filter.CreatedTo is not null)
            Add(pairs, "createdTo", filter.CreatedTo.Value.ToString("o"));

        if (pairs.Count == 0)
            return string.Empty;
        return "?" + string.Join("&", pairs);
    }

    private static void Add(List<string> pairs, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        pairs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
    }

    private async Task<ModuleLifecycleActionResultDto> PostModuleLifecycleActionAsync(
        string route,
        string moduleId,
        string action,
        CancellationToken cancellationToken)
    {
        var response = await _http.PostAsync(route, content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<ModuleLifecycleActionResultDto>(cancellationToken).ConfigureAwait(false);
        return dto ?? new ModuleLifecycleActionResultDto(
            Succeeded: false,
            ModuleId: moduleId,
            Action: action,
            State: "Discovered",
            Effect: "Immediate",
            Blockers: [new ModuleLifecycleBlockerDto("no_response", "Server returned no lifecycle action payload.")]);
    }
}
