using System.Text.Json.Serialization;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.Management.Client;

public interface IManagementClient
{
    Task<IReadOnlyList<ModuleDto>> GetModulesAsync(CancellationToken cancellationToken = default);
    Task<ModuleDto?> GetModuleAsync(string moduleId, CancellationToken cancellationToken = default);
    Task<ModuleLifecycleActionResultDto> InstallModuleAsync(string moduleId, CancellationToken cancellationToken = default);
    Task<ModuleLifecycleActionResultDto> EnableModuleAsync(string moduleId, CancellationToken cancellationToken = default);
    Task<ModuleLifecycleActionResultDto> DisableModuleAsync(string moduleId, CancellationToken cancellationToken = default);
    Task<ModuleLifecycleActionResultDto> UninstallModuleAsync(string moduleId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowDto>> GetWorkflowsAsync(string moduleId, CancellationToken cancellationToken = default);
    Task<StartWorkflowResult> StartWorkflowAsync(
        string workflowId,
        IReadOnlyDictionary<string, object?>? parameters,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RunSummaryDto>> GetRunsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RunSummaryDto>> GetRunsAsync(RunQueryFilter? filter, CancellationToken cancellationToken = default);
    Task<RunDetailDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ControlActionAvailabilityDto>> GetRunActionsAsync(string runId, CancellationToken cancellationToken = default);
    Task<RunTimelineDto?> GetRunTimelineAsync(string runId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationDefinitionDto>> GetOperationsAsync(CancellationToken cancellationToken = default);
    Task<OperationDefinitionDto?> GetOperationAsync(string operationDefinitionId, CancellationToken cancellationToken = default);
    Task<StartOperationResult> StartOperationAsync(
        string operationDefinitionId,
        IReadOnlyDictionary<string, object?>? parameters,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<CancelRunResult> CancelRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<RetryRunResult> RetryRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<ExternalControlResult> ParkRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<ExternalControlResult> ResumeRunAsync(string runId, CancellationToken cancellationToken = default);

    Task<ControlCenterDto> GetControlCenterAsync(CancellationToken cancellationToken = default);

    Task<HostEffectiveModuleSnapshotDto> GetRuntimeCompositionAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RuntimeControlRequestDto>> GetRuntimeShutdownRequestsAsync(
        string runtimeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets audit timeline with optional filters. Epic 32.
    /// </summary>
    Task<AuditTimelinePageDto> GetAuditTimelineAsync(
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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single audit entry by id. Epic 32.
    /// </summary>
    Task<AuditTimelineEntryDto?> GetAuditEntryAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets runtime health snapshot. Epic 30.
    /// </summary>
    Task<RuntimeHealthSnapshotDto?> GetRuntimeHealthAsync(
        string? runtimePlaneId = "local",
        int blockedDeferredWindowHours = 24,
        int scheduleFailureWindowHours = 24,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists schedules. Epic 28/33.
    /// </summary>
    Task<IReadOnlyList<ScheduleDto>> GetSchedulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets schedule detail with execution state.
    /// </summary>
    Task<ScheduleDetailDto?> GetScheduleAsync(string id, string? runtimePlaneId = "local", CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables a schedule.
    /// </summary>
    Task EnableScheduleAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables a schedule.
    /// </summary>
    Task DisableScheduleAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a schedule. Epic 38. Throws <see cref="ScheduleValidationException"/> on 400.
    /// </summary>
    Task<ScheduleDto> CreateScheduleAsync(CreateScheduleRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a schedule. Epic 38. Throws <see cref="ScheduleValidationException"/> on 400.
    /// </summary>
    Task<ScheduleDto> UpdateScheduleAsync(string id, UpdateScheduleRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists blocked work with remediation eligibility. Epic 31.
    /// </summary>
    Task<IReadOnlyList<BlockedWorkRemediationEntryDto>> GetBlockedWorkAsync(
        string? runtimePlaneId = "local",
        int windowHours = 24,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single blocked item's remediation context.
    /// </summary>
    Task<BlockedWorkRemediationEntryDto?> GetBlockedWorkItemAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retries a blocked workflow. Returns 202 on success; 409 if not eligible; 400 for operations (inspection only).
    /// </summary>
    Task<BlockedWorkRetryResult> RetryBlockedWorkAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers global scheduler re-evaluation cycle. Not schedule-specific.
    /// </summary>
    Task TriggerSchedulerRunCycleAsync(string? runtimePlaneId = "local", CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets operator UI capabilities for permission-aware rendering. Epic 34.
    /// </summary>
    Task<OperatorCapabilitiesDto> GetOperatorCapabilitiesAsync(CancellationToken cancellationToken = default);

    // --- Auth / session (Epic 42) ---

    /// <summary>Current user for authenticated session. Returns null when unauthenticated (401). Throws on 403 (principal not linked to platform user).</summary>
    Task<MeResponse?> GetMeAsync(CancellationToken cancellationToken = default);

    /// <summary>Local username/password login. Throws <see cref="HttpRequestException"/> on failure (e.g. 401).</summary>
    Task<LocalLoginResponse> LocalLoginAsync(LocalLoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>Clears auth cookie (204).</summary>
    Task LocalLogoutAsync(CancellationToken cancellationToken = default);

    // --- Governance admin (Epic 39/40) ---

    /// <summary>Gets the governance descriptor catalog (resources and permissions). Requires user.read.</summary>
    Task<GovernanceDescriptorResponse> GetGovernanceDescriptorsAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists all roles. Requires user.read.</summary>
    Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a role by id with its granted permissions. Requires user.read.</summary>
    Task<RoleDetailDto?> GetRoleAsync(string roleId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the role's full permission set. Requires permission.assign. Throws on 400/403/404.</summary>
    Task SetRolePermissionsAsync(string roleId, SetRolePermissionsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gets a paginated page of users. Requires user.read.</summary>
    Task<UserListResponse> GetUserPageAsync(int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Gets a user by id. Requires user.read.</summary>
    Task<UserDetailDto?> GetUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Gets effective access for a user (merged permissions from roles). Requires user.read.</summary>
    Task<PrincipalEffectiveAccessDto?> GetEffectiveAccessAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Assigns a role to a user. Requires role.assign. Throws on 400/403/404.</summary>
    Task AssignRoleAsync(string userId, string roleId, CancellationToken cancellationToken = default);

    /// <summary>Removes a role from a user. Requires role.assign. Throws on 403/404.</summary>
    Task RemoveRoleAsync(string userId, string roleId, CancellationToken cancellationToken = default);
}

public sealed record GovernanceDescriptorResponse(
    IReadOnlyList<ResourceCatalogEntryDto> Resources,
    IReadOnlyList<PermissionCatalogEntryDto> Permissions);

public sealed record ResourceCatalogEntryDto(string ResourceType, string ScopeKey, string? Family);

public sealed record PermissionCatalogEntryDto(
    string Action,
    string? ResourceType,
    string? ScopeKey,
    string? Family,
    string Source,
    string? ModuleId);

public sealed record RoleDto(string Id, string Name, string? Description, bool IsSystemRole);

public sealed record RoleDetailDto(
    string Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    IReadOnlyList<RolePermissionEntryDto> Permissions);

public sealed record RolePermissionEntryDto(string Action, string? ResourceType, string? ScopeKey);

public sealed record RolePermissionDto(string Action, string? ResourceType, string? ScopeKey);

public sealed record SetRolePermissionsRequest(IReadOnlyList<RolePermissionDto> Permissions);

public sealed record UserListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<UserListDto> Items,
    [property: JsonPropertyName("totalCount")] int TotalCount);

public sealed record UserListDto(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("hasLocalLink")] bool HasLocalLink,
    [property: JsonPropertyName("hasEntraLink")] bool HasEntraLink,
    [property: JsonPropertyName("roleIds")] IReadOnlyList<string> RoleIds);

public sealed record UserDetailDto(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("identityLinks")] IReadOnlyList<IdentityLinkDto> IdentityLinks,
    [property: JsonPropertyName("roleIds")] IReadOnlyList<string> RoleIds,
    [property: JsonPropertyName("hasLocalCredential")] bool? HasLocalCredential,
    [property: JsonPropertyName("isCredentialDisabled")] bool? IsCredentialDisabled);

public sealed record IdentityLinkDto(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("externalId")] string ExternalId,
    [property: JsonPropertyName("lastAuthenticatedAt")] DateTimeOffset? LastAuthenticatedAt);

public sealed record PrincipalEffectiveAccessDto(
    string UserId,
    IReadOnlyList<string> RoleIds,
    IReadOnlyList<RolePermissionEntryDto> Permissions);

public sealed record OperatorCapabilitiesDto(
    bool CanManageSchedules,
    bool CanManageRecovery,
    bool CanViewAudit,
    bool CanViewRuntimeHealth,
    bool CanManageModules,
    bool CanViewGovernance = false,
    bool CanAssignPermissions = false,
    bool CanAssignRoles = false);

public sealed record BlockedWorkRetryResult(
    bool Success,
    string? NewRunId,
    string? ErrorMessage,
    bool InspectionOnly);

public sealed record AuditTimelineEntryDto(
    string Id,
    DateTimeOffset Timestamp,
    string Actor,
    string Action,
    string Category,
    string Outcome,
    string Summary,
    string? Details,
    IReadOnlyList<AuditRelatedReferenceDto> RelatedReferences);

public sealed record AuditRelatedReferenceDto(string Kind, string Id);

public sealed record AuditTimelinePageDto(
    IReadOnlyList<AuditTimelineEntryDto> Items,
    string? NextContinuationToken);

public sealed record StartWorkflowResult(
    string RunId,
    string WorkflowInstanceId,
    string WorkflowDefinitionId,
    string Status);

public sealed record RunSummaryDto(
    string RunId,
    string ExecutionInstanceId,
    string ExecutionDefinitionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? CorrelationKey,
    string? RetryOfRunId,
    string ExecutionKind = "workflow");

public sealed record RunQueryFilter(
    string? ExecutionKind = null,
    string? OperationDefinitionId = null,
    string? Status = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    string? ModuleId = null,
    string? CorrelationKey = null);

/// <summary>Epic 35: investigation navigation hint. Kind is stable; TargetId optional. UI must not link when TargetId is null.</summary>
public sealed record RunInvestigationHintDto(string Kind, string? TargetId);

/// <summary>Epic 35: run forensics from persisted data and conventions. ModuleId/ScheduleId/Origin are derived or inferred; RetriedByRunIds is direct; ModuleExecutableNow is current snapshot only.</summary>
public sealed record RunForensicContextDto(
    string? ModuleId,
    string OriginKind,
    string OriginDetail,
    string? ScheduleId,
    DateTimeOffset? ScheduleFireTimeUtc,
    IReadOnlyList<string> RetriedByRunIds,
    bool? ModuleExecutableNow,
    string? ModuleExecutionHint,
    IReadOnlyList<RunInvestigationHintDto> InvestigationHints);

public sealed record RunDetailDto(
    string RunId,
    string ExecutionInstanceId,
    string ExecutionDefinitionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? CorrelationKey,
    string? RetryOfRunId,
    string? CurrentStepName,
    RunResultDto? Result,
    RunErrorDto? Error,
    IReadOnlyList<RunLifecycleEventDto> LifecycleEvents,
    IReadOnlyList<RunStepInstanceDto> StepInstances,
    IReadOnlyList<RunControlRequestDto> ControlRequests,
    string? StartIdempotencyKey,
    string? StartCorrelationId,
    string? StartParametersJson,
    DateTimeOffset? StartRequestedAt,
    string? StartClaimedBy,
    DateTimeOffset? StartClaimedAt,
    string ExecutionKind = "workflow",
    RunForensicContextDto? ForensicContext = null);

public sealed record RunResultDto(
    string Outcome,
    string Summary,
    string? PayloadJson,
    string? DiagnosticsJson,
    string? OutputAvailability = null,
    string? OutputDisplayPolicy = null,
    string? DiagnosticsAvailability = null,
    string? DiagnosticsDisplayPolicy = null);

public sealed record RunErrorDto(
    string Category,
    string Message,
    string? Code,
    string? DetailsJson);

public sealed record RunLifecycleEventDto(
    int Id,
    string EventType,
    string? FromStatus,
    string? ToStatus,
    DateTimeOffset OccurredAt,
    string? RelatedRunId,
    string? StepName,
    string? DetailsJson);

public sealed record RunStepInstanceDto(
    string StepName,
    int Attempt,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    string? Error);

public sealed record RunControlRequestDto(
    long Id,
    string ControlKind,
    string Status,
    DateTimeOffset RequestedAt,
    string? RequestedBy,
    DateTimeOffset? AppliedAt,
    string? AppliedBy,
    string? LastError);

public sealed record RunTimelineDto(
    string RunId,
    string ExecutionKind,
    IReadOnlyList<RunTimelineEventDto> Events);

public sealed record RunTimelineEventDto(
    string EventId,
    string RunId,
    DateTimeOffset OccurredAt,
    string EventKind,
    string Summary,
    string? Actor,
    string SourceType,
    string? SourceId,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record CancelRunResult(string RunId, string Status);

public sealed record StartOperationResult(
    string RunId,
    string OperationInstanceId,
    string OperationDefinitionId,
    string Status);

public sealed record OperationDefinitionDto(
    string OperationDefinitionId,
    string? ModuleId,
    string InvariantName,
    string DisplayName,
    string? Description,
    string Category,
    bool CanStart,
    OperationInputSchema? InputSchema);

public sealed record OperationInputSchema(IReadOnlyList<OperationInputFieldDto> Fields);

public sealed record OperationInputFieldDto(
    string Key,
    string Type,
    string Label,
    string? HelpText,
    bool Required,
    string? DefaultValue,
    int Order,
    IReadOnlyList<string>? AllowedValues,
    decimal? Min,
    decimal? Max,
    int? MaxLength);

public sealed record RetryRunResult(
    string OriginalRunId,
    string RunId,
    string WorkflowInstanceId,
    string WorkflowDefinitionId,
    string Status,
    string RetryOfRunId);

public sealed record ExternalControlResult(
    string RunId,
    string WorkflowInstanceId,
    string WorkflowDefinitionId,
    string ControlKind,
    string Status,
    string RunStatus,
    string? CurrentStepName);

public sealed record ControlActionAvailabilityDto(
    string ActionKind,
    string AvailabilityState,
    string? ReasonCode);

public sealed record ControlTargetSummaryDto(
    string TargetKind,
    string TargetId,
    string DisplayName,
    string? GroupKey,
    string? DetailLink,
    IReadOnlyList<ControlActionAvailabilityDto> AvailableActions);

public sealed record ControlActivitySummaryDto(
    string ActivityKind,
    string ActivityId,
    string TargetKind,
    string TargetId,
    string RequestedAtIso,
    string? CompletedAtIso,
    string CurrentNormalizedStatus,
    string Outcome,
    string OutcomeSummary,
    string ActivitySource,
    string DetailLink,
    IReadOnlyList<ControlActionAvailabilityDto> AvailableActions);

public sealed record ControlAttentionItemDto(
    string AttentionKind,
    string Severity,
    string TargetKind,
    string TargetId,
    string Summary,
    string CreatedAtIso,
    string? RelatedActivityId);

public sealed record ControlCenterDto(
    IReadOnlyList<ControlTargetSummaryDto> Targets,
    IReadOnlyList<ControlActivitySummaryDto> RecentActivity,
    IReadOnlyList<ControlAttentionItemDto> AttentionItems);

public sealed record RuntimeControlRequestDto(
    long Id,
    string RuntimeId,
    string ControlKind,
    string Status,
    DateTimeOffset RequestedAt,
    string? RequestedBy,
    DateTimeOffset? AppliedAt,
    string? AppliedBy,
    string? LastError);

// --- Auth (Epic 42), aligned with Management.Api AuthController ---

public sealed record MeResponse(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles);

public sealed record LocalLoginRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string? Password);

public sealed record LocalLoginResponse(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("displayName")] string DisplayName);
