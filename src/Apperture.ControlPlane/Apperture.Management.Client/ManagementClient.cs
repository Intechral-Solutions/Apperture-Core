using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Apperture.Core.Domain.Workflow;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.Management.Client;

public sealed class ManagementClient : IManagementClient
{
    /// <summary>
    /// Matches ASP.NET Core default JSON (camelCase) and tolerates casing drift so capability flags deserialize
    /// reliably; otherwise all booleans default to false and the UI caches deny-all.
    /// </summary>
    private static readonly JsonSerializerOptions ApiJsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Serialize request DTO property names as camelCase; keep dictionary keys literal so connector field keys are not rewritten.
    /// </summary>
    private static readonly JsonSerializerOptions ApiJsonWriteOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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

    public async Task<DashboardDefaultDto> GetDashboardDefaultAsync(CancellationToken cancellationToken = default)
    {
        var dto = await _http.GetFromJsonAsync<DashboardDefaultDto>("dashboard/default", cancellationToken).ConfigureAwait(false);
        return dto ?? new DashboardDefaultDto("shared-platform-operator", "Platform Summary", []);
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

    public async Task<IReadOnlyList<ConnectionDto>> GetConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<ConnectionDto[]>("connections", ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<ConnectionDto>();
    }

    public async Task<ConnectionDto?> GetConnectionAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<ConnectionDto>(
            $"connections/{Uri.EscapeDataString(id)}",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConnectionDto> CreateConnectionAsync(CreateConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("connections", request, ApiJsonWriteOptions, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);
        var created = await response.Content.ReadFromJsonAsync<ConnectionDto>(ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
        return created ?? throw new InvalidOperationException("Server returned no connection body.");
    }

    public async Task<ConnectionDto> UpdateConnectionAsync(string id, UpdateConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PatchAsJsonAsync(
            $"connections/{Uri.EscapeDataString(id)}",
            request,
            ApiJsonWriteOptions,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);
        var updated = await response.Content.ReadFromJsonAsync<ConnectionDto>(ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
        return updated ?? throw new InvalidOperationException("Server returned no connection body.");
    }

    public async Task RetireConnectionAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _http.DeleteAsync(
            $"connections/{Uri.EscapeDataString(id)}",
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConnectionDto> TestConnectionAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync(
            $"connections/{Uri.EscapeDataString(id)}/test-connect",
            content: null,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<ConnectionDto>(ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Server returned no connection body.");
    }

    public async Task<IReadOnlyList<ConnectorDefinitionCatalogDto>> GetConnectorDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<ConnectorDefinitionCatalogDto[]>(
            "connector-definitions",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<ConnectorDefinitionCatalogDto>();
    }

    public async Task<ConnectorDefinitionCatalogDto?> GetConnectorDefinitionAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<ConnectorDefinitionCatalogDto>(
            $"connector-definitions/{Uri.EscapeDataString(id)}",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IntegrationDto>> GetIntegrationsAsync(CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<IntegrationDto[]>("integrations", ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<IntegrationDto>();
    }

    public async Task<IReadOnlyList<IntegrationFleetRowDto>> GetIntegrationFleetAsync(
        IntegrationFleetQueryParams? query = null,
        CancellationToken cancellationToken = default)
    {
        var path = BuildIntegrationFleetPath(query);
        var list = await _http.GetFromJsonAsync<IntegrationFleetRowDto[]>(path, ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<IntegrationFleetRowDto>();
    }

    private static string BuildIntegrationFleetPath(IntegrationFleetQueryParams? q)
    {
        if (q is null)
            return "integrations/fleet";
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(q.State))
            parts.Add($"state={Uri.EscapeDataString(q.State)}");
        if (q.Runnable is not null)
            parts.Add($"runnable={(q.Runnable.Value ? "true" : "false")}");
        if (q.HasBlocking is not null)
            parts.Add($"hasBlocking={(q.HasBlocking.Value ? "true" : "false")}");
        if (q.HasAdvisory is not null)
            parts.Add($"hasAdvisory={(q.HasAdvisory.Value ? "true" : "false")}");
        if (q.VerificationConcern is not null)
            parts.Add($"verificationConcern={(q.VerificationConcern.Value ? "true" : "false")}");
        if (!string.IsNullOrWhiteSpace(q.Verification))
            parts.Add($"verification={Uri.EscapeDataString(q.Verification)}");
        if (q.HasOperationalFailure is not null)
            parts.Add($"hasOperationalFailure={(q.HasOperationalFailure.Value ? "true" : "false")}");
        if (q.HasUnexpectedSilence is not null)
            parts.Add($"hasUnexpectedSilence={(q.HasUnexpectedSilence.Value ? "true" : "false")}");
        if (!string.IsNullOrWhiteSpace(q.Attention))
            parts.Add($"attention={Uri.EscapeDataString(q.Attention)}");
        if (!string.IsNullOrWhiteSpace(q.Sort))
            parts.Add($"sort={Uri.EscapeDataString(q.Sort)}");
        return parts.Count == 0 ? "integrations/fleet" : $"integrations/fleet?{string.Join("&", parts)}";
    }

    public async Task<IntegrationDto?> GetIntegrationAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<IntegrationDto>(
            $"integrations/{Uri.EscapeDataString(id)}",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IntegrationScheduleDiagnosticsDto?> GetIntegrationScheduleDiagnosticsAsync(
        string integrationId,
        CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<IntegrationScheduleDiagnosticsDto>(
            $"integrations/{Uri.EscapeDataString(integrationId)}/schedule-diagnostics",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
    }


    public async Task<AttentionRoutingEffectiveDto?> GetIntegrationAttentionRoutingAsync(
        string integrationId,
        string? conditionKey = null,
        CancellationToken cancellationToken = default)
    {
        var q = string.IsNullOrWhiteSpace(conditionKey)
            ? ""
            : $"?conditionKey={Uri.EscapeDataString(conditionKey)}";
        return await _http.GetFromJsonAsync<AttentionRoutingEffectiveDto>(
            $"integrations/{Uri.EscapeDataString(integrationId)}/attention-routing{q}",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task PutIntegrationAttentionRoutingAsync(
        string integrationId,
        UpsertAttentionRoutingRequest body,
        string? conditionKey = null,
        CancellationToken cancellationToken = default)
    {
        var q = string.IsNullOrWhiteSpace(conditionKey)
            ? ""
            : $"?conditionKey={Uri.EscapeDataString(conditionKey)}";
        var response = await _http
            .PutAsJsonAsync(
                $"integrations/{Uri.EscapeDataString(integrationId)}/attention-routing{q}",
                body,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task<AttentionRoutingEffectiveDto?> GetAttentionRoutingDefaultsAsync(
        string? conditionKey = null,
        CancellationToken cancellationToken = default)
    {
        var q = string.IsNullOrWhiteSpace(conditionKey)
            ? ""
            : $"?conditionKey={Uri.EscapeDataString(conditionKey)}";
        return await _http.GetFromJsonAsync<AttentionRoutingEffectiveDto>(
            $"attention-routing/defaults{q}",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PutAttentionRoutingDefaultsAsync(
        UpsertAttentionRoutingRequest body,
        string? conditionKey = null,
        CancellationToken cancellationToken = default)
    {
        var q = string.IsNullOrWhiteSpace(conditionKey)
            ? ""
            : $"?conditionKey={Uri.EscapeDataString(conditionKey)}";
        var response = await _http
            .PutAsJsonAsync($"attention-routing/defaults{q}", body, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AttentionContactGroupSummaryDto>> ListAttentionContactGroupsAsync(
        bool includeDisabled = false,
        CancellationToken cancellationToken = default)
    {
        var q = includeDisabled ? "?includeDisabled=true" : "";
        var rows = await _http.GetFromJsonAsync<List<AttentionContactGroupSummaryDto>>(
            $"attention-contact-groups{q}",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
        return rows ?? new List<AttentionContactGroupSummaryDto>();
    }

    /// <inheritdoc />
    public async Task<AttentionContactGroupDetailDto?> GetAttentionContactGroupAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<AttentionContactGroupDetailDto>(
            $"attention-contact-groups/{Uri.EscapeDataString(id)}",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AttentionContactGroupDetailDto> CreateAttentionContactGroupAsync(
        CreateAttentionContactGroupRequest body,
        CancellationToken cancellationToken = default)
    {
        var response = await _http
            .PostAsJsonAsync("attention-contact-groups", body, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AttentionContactGroupDetailDto>(
                ApiJsonReadOptions,
                cancellationToken).ConfigureAwait(false))!;
    }

    /// <inheritdoc />
    public async Task<AttentionContactGroupDetailDto?> UpdateAttentionContactGroupAsync(
        string id,
        UpdateAttentionContactGroupRequest body,
        CancellationToken cancellationToken = default)
    {
        var response = await _http
            .PutAsJsonAsync($"attention-contact-groups/{Uri.EscapeDataString(id)}", body, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AttentionContactGroupDetailDto>(
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AttentionContactGroupDetailDto?> ReplaceAttentionContactGroupMembersAsync(
        string id,
        ReplaceAttentionContactGroupMembersRequest body,
        CancellationToken cancellationToken = default)
    {
        var response = await _http
            .PutAsJsonAsync(
                $"attention-contact-groups/{Uri.EscapeDataString(id)}/members",
                body,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AttentionContactGroupDetailDto>(
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperationalTeamSummaryDto>> ListOperationalTeamsAsync(
        bool includeDisabled = false,
        CancellationToken cancellationToken = default)
    {
        var q = includeDisabled ? "?includeDisabled=true" : "";
        var rows = await _http.GetFromJsonAsync<List<OperationalTeamSummaryDto>>(
            $"operational-teams{q}",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
        return rows ?? new List<OperationalTeamSummaryDto>();
    }

    /// <inheritdoc />
    public async Task<OperationalTeamDetailDto?> GetOperationalTeamAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<OperationalTeamDetailDto>(
            $"operational-teams/{Uri.EscapeDataString(id)}",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationalTeamDetailDto> CreateOperationalTeamAsync(
        CreateOperationalTeamRequest body,
        CancellationToken cancellationToken = default)
    {
        var response = await _http
            .PostAsJsonAsync("operational-teams", body, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OperationalTeamDetailDto>(
                ApiJsonReadOptions,
                cancellationToken).ConfigureAwait(false))!;
    }

    /// <inheritdoc />
    public async Task<OperationalTeamDetailDto?> UpdateOperationalTeamAsync(
        string id,
        UpdateOperationalTeamRequest body,
        CancellationToken cancellationToken = default)
    {
        var response = await _http
            .PutAsJsonAsync($"operational-teams/{Uri.EscapeDataString(id)}", body, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OperationalTeamDetailDto>(
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationalTeamDetailDto?> ReplaceOperationalTeamMembersAsync(
        string id,
        ReplaceOperationalTeamMembersRequest body,
        CancellationToken cancellationToken = default)
    {
        var response = await _http
            .PutAsJsonAsync($"operational-teams/{Uri.EscapeDataString(id)}/members", body, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OperationalTeamDetailDto>(
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> AcknowledgeIntegrationAttentionIncidentAsync(
        string integrationId,
        string incidentId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
                $"integrations/{Uri.EscapeDataString(integrationId)}/attention-incidents/{Uri.EscapeDataString(incidentId)}/acknowledge",
                new AcknowledgeAttentionIncidentRequest(note),
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<IntegrationOperatorDetailDto?> GetIntegrationOperatorDetailAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<IntegrationOperatorDetailDto>(
            $"integrations/{Uri.EscapeDataString(id)}/detail",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IntegrationProcedureListDto?> GetIntegrationProceduresAsync(string integrationId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync(
            $"integrations/{Uri.EscapeDataString(integrationId)}/procedures",
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IntegrationProcedureListDto>(ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IntegrationProcedureDetailDto?> GetIntegrationProcedureAsync(
        string integrationId,
        string procedureId,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync(
            $"integrations/{Uri.EscapeDataString(integrationId)}/procedures/{Uri.EscapeDataString(procedureId)}",
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IntegrationProcedureDetailDto>(ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IntegrationProcedureLaunchClientResult> LaunchIntegrationProcedureAsync(
        string integrationId,
        string procedureId,
        IReadOnlyDictionary<string, object?>? parameters,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"integrations/{Uri.EscapeDataString(integrationId)}/procedures/{Uri.EscapeDataString(procedureId)}/launch",
            new { parameters, idempotencyKey, correlationId },
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);

        var status = (int)response.StatusCode;
        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            var dto = await response.Content.ReadFromJsonAsync<IntegrationProcedureLaunchResultDto>(ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
            return new IntegrationProcedureLaunchClientResult(true, dto, status, null, null);
        }

        var (code, message) = await TryReadProcedureErrorBodyAsync(response, cancellationToken).ConfigureAwait(false);
        return new IntegrationProcedureLaunchClientResult(false, null, status, code, message ?? response.ReasonPhrase);
    }

    private static async Task<(string? Code, string? Error)> TryReadProcedureErrorBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;
            string? code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
            string? error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (code, error);
        }
        catch
        {
            return (null, null);
        }
    }

    public async Task<IntegrationDto> CreateIntegrationAsync(CreateIntegrationRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("integrations", request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IntegrationDto>(ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Unexpected null response from CreateIntegration.");
    }

    public async Task<IntegrationDto> UpdateIntegrationAsync(string id, UpdateIntegrationRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PatchAsJsonAsync($"integrations/{Uri.EscapeDataString(id)}", request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IntegrationDto>(ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Unexpected null response from UpdateIntegration.");
    }

    public async Task DeleteIntegrationAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _http.DeleteAsync($"integrations/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IntegrationDto> ActivateIntegrationAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"integrations/{Uri.EscapeDataString(id)}/activate", content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IntegrationDto>(ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Unexpected null response from ActivateIntegration.");
    }

    public async Task<IntegrationDto> DeactivateIntegrationAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"integrations/{Uri.EscapeDataString(id)}/deactivate", content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IntegrationDto>(ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Unexpected null response from DeactivateIntegration.");
    }

    public async Task<IReadOnlyList<IntegrationBindingDto>> GetIntegrationBindingsAsync(string integrationId, CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<IntegrationBindingDto[]>(
            $"integrations/{Uri.EscapeDataString(integrationId)}/bindings",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<IntegrationBindingDto>();
    }

    public async Task<IntegrationBindingDto> CreateIntegrationBindingAsync(string integrationId, CreateIntegrationBindingRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"integrations/{Uri.EscapeDataString(integrationId)}/bindings",
            request,
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IntegrationBindingDto>(ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Unexpected null response from CreateIntegrationBinding.");
    }

    public async Task DeleteIntegrationBindingAsync(string integrationId, string bindingId, CancellationToken cancellationToken = default)
    {
        var response = await _http.DeleteAsync(
            $"integrations/{Uri.EscapeDataString(integrationId)}/bindings/{Uri.EscapeDataString(bindingId)}",
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<EligibleConnectionDto>> GetIntegrationEligibleConnectionsAsync(string integrationId, string slotName, string contractType, CancellationToken cancellationToken = default)
    {
        var query = $"?slotName={Uri.EscapeDataString(slotName)}&contractType={Uri.EscapeDataString(contractType)}";
        var list = await _http.GetFromJsonAsync<EligibleConnectionDto[]>(
            $"integrations/{Uri.EscapeDataString(integrationId)}/bindings/eligible-connections{query}",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<EligibleConnectionDto>();
    }

    public async Task<IReadOnlyList<ModuleSlotDto>> GetIntegrationModuleSlotsAsync(string integrationId, CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<ModuleSlotDto[]>(
            $"integrations/{Uri.EscapeDataString(integrationId)}/bindings/slots",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<ModuleSlotDto>();
    }

    public async Task<IReadOnlyList<ScheduleDto>> GetIntegrationSchedulesAsync(string integrationId, CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<ScheduleDto[]>(
            $"integrations/{Uri.EscapeDataString(integrationId)}/schedules",
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<ScheduleDto>();
    }

    public async Task<ScheduleDto> CreateIntegrationScheduleAsync(string integrationId, CreateIntegrationScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"integrations/{Uri.EscapeDataString(integrationId)}/schedules",
            request,
            ApiJsonReadOptions,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ScheduleDto>(ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Unexpected null response from CreateIntegrationSchedule.");
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

    public async Task<ScheduleDto> CreateScheduleAsync(CreateScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            request.Name,
            request.RecurrenceExpression,
            TimeZone = request.TimeZone ?? "UTC",
            request.Enabled,
            TargetJobType = request.TargetJobType ?? 0,
            request.TargetJobConfiguration,
            request.ExecutionPolicyRef,
            RuntimePlaneId = request.RuntimePlaneId ?? "local",
            request.IntegrationId
        };
        var response = await _http.PostAsJsonAsync("schedules", body, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            throw await ParseValidationExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<ScheduleDto>(cancellationToken).ConfigureAwait(false);
        return created ?? throw new InvalidOperationException("Server returned no schedule body.");
    }

    public async Task<ScheduleDto> UpdateScheduleAsync(string id, UpdateScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            request.Name,
            request.RecurrenceExpression,
            request.TimeZone,
            request.Enabled
        };
        var response = await _http.PatchAsJsonAsync($"schedules/{Uri.EscapeDataString(id)}", body, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            throw await ParseValidationExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<ScheduleDto>(cancellationToken).ConfigureAwait(false);
        return updated ?? throw new InvalidOperationException("Server returned no schedule body.");
    }

    private static async Task<ScheduleValidationException> ParseValidationExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
            if (json.TryGetProperty("errors", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in arr.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String)
                        errors.Add(e.GetString() ?? "Unknown error");
            }
        }
        catch
        {
            errors.Add(response.ReasonPhrase ?? "Validation failed.");
        }
        if (errors.Count == 0)
            errors.Add(response.ReasonPhrase ?? "Validation failed.");
        return new ScheduleValidationException(errors);
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

    public async Task<GovernanceDescriptorResponse> GetGovernanceDescriptorsAsync(CancellationToken cancellationToken = default)
    {
        var dto = await _http.GetFromJsonAsync<GovernanceDescriptorResponse>("governance/descriptors", cancellationToken).ConfigureAwait(false);
        return dto ?? new GovernanceDescriptorResponse([], []);
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<RoleDto[]>("roles", cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<RoleDto>();
    }

    public async Task<RoleDetailDto?> GetRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<RoleDetailDto>($"roles/{Uri.EscapeDataString(roleId)}", cancellationToken).ConfigureAwait(false);
    }

    public async Task SetRolePermissionsAsync(string roleId, SetRolePermissionsRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PutAsJsonAsync($"roles/{Uri.EscapeDataString(roleId)}/permissions", request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.NotFound)
        {
            var msg = await TryGetMessageFromResponseAsync(response, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(msg ?? "Request failed.");
        }
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new HttpRequestException("Not authorized to assign permissions.", null, HttpStatusCode.Forbidden);
        response.EnsureSuccessStatusCode();
    }

    public async Task<UserListResponse> GetUserPageAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = $"?skip={skip}&take={take}";
        var dto = await _http.GetFromJsonAsync<UserListResponse>($"users{query}", cancellationToken).ConfigureAwait(false);
        return dto ?? new UserListResponse([], 0);
    }

    public async Task<UserDetailDto?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<UserDetailDto>($"users/{Uri.EscapeDataString(userId)}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<PrincipalEffectiveAccessDto?> GetEffectiveAccessAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<PrincipalEffectiveAccessDto>($"users/{Uri.EscapeDataString(userId)}/effective-access", cancellationToken).ConfigureAwait(false);
    }

    public async Task AssignRoleAsync(string userId, string roleId, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"users/{Uri.EscapeDataString(userId)}/roles",
            new { roleId },
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.NotFound)
        {
            var msg = await TryGetMessageFromResponseAsync(response, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(msg ?? "Request failed.");
        }
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new HttpRequestException("Not authorized to assign roles.", null, HttpStatusCode.Forbidden);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveRoleAsync(string userId, string roleId, CancellationToken cancellationToken = default)
    {
        var response = await _http.DeleteAsync(
            $"users/{Uri.EscapeDataString(userId)}/roles/{Uri.EscapeDataString(roleId)}",
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var msg = await TryGetMessageFromResponseAsync(response, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(msg ?? "User or role not found.");
        }
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new HttpRequestException("Not authorized to remove roles.", null, HttpStatusCode.Forbidden);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string?> TryGetMessageFromResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
            if (json.TryGetProperty("message", out var prop))
                return prop.GetString();
        }
        catch { }
        return null;
    }

    public async Task<OperatorCapabilitiesDto> GetOperatorCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("control/capabilities", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<OperatorCapabilitiesDto>(ApiJsonReadOptions, cancellationToken).ConfigureAwait(false);
        return dto ?? new OperatorCapabilitiesDto(false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false);
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
        Add(pairs, "integrationId", filter.IntegrationId);
        if (filter.CreatedFrom is not null)
            Add(pairs, "createdFrom", filter.CreatedFrom.Value.ToString("o"));
        if (filter.CreatedTo is not null)
            Add(pairs, "createdTo", filter.CreatedTo.Value.ToString("o"));
        if (filter.Take is int take && take > 0)
            pairs.Add($"take={take.ToString(CultureInfo.InvariantCulture)}");

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

    public async Task<MeResponse?> GetMeAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("auth/me", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return null;
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new HttpRequestException(
                "Authenticated principal is not linked to a platform user.",
                null,
                HttpStatusCode.Forbidden);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MeResponse>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalLoginResponse> LocalLoginAsync(LocalLoginRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("auth/local/login", request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new HttpRequestException("Login failed.", null, HttpStatusCode.Unauthorized);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<LocalLoginResponse>(cancellationToken).ConfigureAwait(false);
        return dto ?? throw new InvalidOperationException("Login response was empty.");
    }

    public async Task LocalLogoutAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync("auth/local/logout", content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<AuthSignInOptionsDto> GetAuthSignInOptionsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("auth/sign-in-options", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<AuthSignInOptionsDto>(cancellationToken).ConfigureAwait(false);
        return dto ?? throw new InvalidOperationException("sign-in-options response was empty.");
    }

    public async Task<AuthUiPolicyDto?> GetAuthUiPolicyAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("config/auth-ui-policy", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthUiPolicyDto>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuthUiPolicyDto> PatchAuthUiPolicyAsync(UpdateAuthUiPolicyRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PatchAsJsonAsync("config/auth-ui-policy", request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.Forbidden)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase : detail,
                null,
                response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<AuthUiPolicyDto>(cancellationToken).ConfigureAwait(false);
        return dto ?? throw new InvalidOperationException("auth-ui-policy patch response was empty.");
    }

    public async Task<ConfigAuthOverviewDto?> GetConfigAuthOverviewAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("config/auth-overview", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ConfigAuthOverviewDto>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntraTenantListItemDto>?> GetEntraTenantsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("config/auth/entra-tenants", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return null;
        response.EnsureSuccessStatusCode();
        var list = await response.Content.ReadFromJsonAsync<EntraTenantListItemDto[]>(cancellationToken).ConfigureAwait(false);
        return list ?? Array.Empty<EntraTenantListItemDto>();
    }

    public async Task<EntraTenantListItemDto?> GetEntraTenantAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"config/auth/entra-tenants/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EntraTenantListItemDto>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<EntraTenantListItemDto> CreateEntraTenantAsync(CreateEntraTenantRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("config/auth/entra-tenants", request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase : detail,
                null,
                response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<EntraTenantListItemDto>(cancellationToken).ConfigureAwait(false);
        return dto ?? throw new InvalidOperationException("create entra tenant response was empty.");
    }

    public async Task<EntraTenantListItemDto> PatchEntraTenantAsync(string id, UpdateEntraTenantRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PatchAsJsonAsync(
            $"config/auth/entra-tenants/{Uri.EscapeDataString(id)}",
            request,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.Conflict)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase : detail,
                null,
                response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<EntraTenantListItemDto>(cancellationToken).ConfigureAwait(false);
        return dto ?? throw new InvalidOperationException("patch entra tenant response was empty.");
    }

    public async Task DeleteEntraTenantAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync($"config/auth/entra-tenants/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase : detail,
                null,
                response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
    }

    public async Task ActivateEntraTenantAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsync(
            $"config/auth/entra-tenants/{Uri.EscapeDataString(id)}/activate",
            content: null,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase : detail,
                null,
                response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
    }

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = await TryReadManagementApiErrorAsync(response, cancellationToken).ConfigureAwait(false);
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"{(int)response.StatusCode} {response.ReasonPhrase}"
            : detail;
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static async Task<string?> TryReadManagementApiErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? raw;
        try
        {
            raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        return ParseManagementApiErrorPayload(raw);
    }

    /// <summary>
    /// Extracts a human-readable message from API error JSON (anonymous <c>{ error }</c>, ProblemDetails, validation <c>errors</c>).
    /// Uses string-based parsing so Blazor WASM and case/culture variations behave consistently.
    /// </summary>
    private static string? ParseManagementApiErrorPayload(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.TrimStart();
        if (trimmed.Length == 0)
            return null;
        if (trimmed[0] != '{' && trimmed[0] != '[')
            return raw.Length > 512 ? raw[..512] + "…" : raw;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return raw.Length > 512 ? raw[..512] + "…" : raw;

            if (TryGetPropertyIgnoreCase(root, "error", out var errEl) && errEl.ValueKind == JsonValueKind.String)
            {
                var s = errEl.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }

            string? title = null;
            if (TryGetPropertyIgnoreCase(root, "title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String)
                title = titleEl.GetString();

            var details = FormatValidationErrorsIgnoreCase(root);
            if (!string.IsNullOrWhiteSpace(details))
                return string.IsNullOrWhiteSpace(title) ? details : $"{title}: {details}";

            if (!string.IsNullOrWhiteSpace(title))
                return title;

            if (TryGetPropertyIgnoreCase(root, "detail", out var detailEl) && detailEl.ValueKind == JsonValueKind.String)
            {
                var d = detailEl.GetString();
                if (!string.IsNullOrWhiteSpace(d))
                    return d;
            }
        }
        catch (JsonException)
        {
            return raw.Length > 512 ? raw[..512] + "…" : raw;
        }

        return raw.Length > 512 ? raw[..512] + "…" : raw;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? FormatValidationErrorsIgnoreCase(JsonElement root)
    {
        if (!TryGetPropertyIgnoreCase(root, "errors", out var errors) || errors.ValueKind != JsonValueKind.Object)
            return null;

        var parts = new List<string>();
        foreach (var field in errors.EnumerateObject())
        {
            if (field.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in field.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var msg = item.GetString();
                        if (!string.IsNullOrWhiteSpace(msg))
                            parts.Add($"{field.Name}: {msg}");
                    }
                }
            }
            else if (field.Value.ValueKind == JsonValueKind.String)
            {
                var msg = field.Value.GetString();
                if (!string.IsNullOrWhiteSpace(msg))
                    parts.Add($"{field.Name}: {msg}");
            }
        }

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }
}
