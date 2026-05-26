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
        string integrationId,
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
        string integrationId,
        IReadOnlyDictionary<string, object?>? parameters,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<CancelRunResult> CancelRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<RetryRunResult> RetryRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<ExternalControlResult> ParkRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<ExternalControlResult> ResumeRunAsync(string runId, CancellationToken cancellationToken = default);

    Task<ControlCenterDto> GetControlCenterAsync(CancellationToken cancellationToken = default);
    Task<DashboardDefaultDto> GetDashboardDefaultAsync(CancellationToken cancellationToken = default);

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
    /// Lists connections with Used In projection. Epic 48.
    /// </summary>
    Task<IReadOnlyList<ConnectionDto>> GetConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a connection by id with Used In projection. Epic 48.
    /// </summary>
    Task<ConnectionDto?> GetConnectionAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a first-class connection resource. Epic 58.
    /// </summary>
    Task<ConnectionDto> CreateConnectionAsync(CreateConnectionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates connection metadata, config, and/or secret material.
    /// </summary>
    Task<ConnectionDto> UpdateConnectionAsync(string id, UpdateConnectionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires a connection (soft delete for eligibility).
    /// </summary>
    Task RetireConnectionAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes connector-specific test-connect and returns updated connection projection.
    /// </summary>
    Task<ConnectionDto> TestConnectionAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists connector definitions (catalog metadata; not executable client operations). Epic 52.
    /// </summary>
    Task<IReadOnlyList<ConnectorDefinitionCatalogDto>> GetConnectorDefinitionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a connector definition by id. Epic 52.
    /// </summary>
    Task<ConnectorDefinitionCatalogDto?> GetConnectorDefinitionAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists integrations. Epic 49.
    /// </summary>
    Task<IReadOnlyList<IntegrationDto>> GetIntegrationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fleet overview with readiness and attention signals. Epic 61.
    /// </summary>
    Task<IReadOnlyList<IntegrationFleetRowDto>> GetIntegrationFleetAsync(
        IntegrationFleetQueryParams? query = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an integration by id. Epic 49.
    /// </summary>
    Task<IntegrationDto?> GetIntegrationAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator schedule diagnostics (expected-fire truth + grace evidence). Epic 67.
    /// </summary>
    Task<IntegrationScheduleDiagnosticsDto?> GetIntegrationScheduleDiagnosticsAsync(
        string integrationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Epic 68: records operator acknowledgement for an attention incident on the integration.
    /// </summary>
    /// <summary>
    /// Epic 70: effective attention routing for an integration condition (default expected_run_missing).
    /// </summary>
    Task<AttentionRoutingEffectiveDto?> GetIntegrationAttentionRoutingAsync(
        string integrationId,
        string? conditionKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Epic 70: upsert integration-level attention routing override.
    /// </summary>
    Task PutIntegrationAttentionRoutingAsync(
        string integrationId,
        UpsertAttentionRoutingRequest body,
        string? conditionKey = null,
        CancellationToken cancellationToken = default);

    Task<AttentionRoutingEffectiveDto?> GetAttentionRoutingDefaultsAsync(
        string? conditionKey = null,
        CancellationToken cancellationToken = default);

    Task PutAttentionRoutingDefaultsAsync(
        UpsertAttentionRoutingRequest body,
        string? conditionKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttentionContactGroupSummaryDto>> ListAttentionContactGroupsAsync(
        bool includeDisabled = false,
        CancellationToken cancellationToken = default);

    Task<AttentionContactGroupDetailDto?> GetAttentionContactGroupAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<AttentionContactGroupDetailDto> CreateAttentionContactGroupAsync(
        CreateAttentionContactGroupRequest body,
        CancellationToken cancellationToken = default);

    Task<AttentionContactGroupDetailDto?> UpdateAttentionContactGroupAsync(
        string id,
        UpdateAttentionContactGroupRequest body,
        CancellationToken cancellationToken = default);

    Task<AttentionContactGroupDetailDto?> ReplaceAttentionContactGroupMembersAsync(
        string id,
        ReplaceAttentionContactGroupMembersRequest body,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationalTeamSummaryDto>> ListOperationalTeamsAsync(
        bool includeDisabled = false,
        CancellationToken cancellationToken = default);

    Task<OperationalTeamDetailDto?> GetOperationalTeamAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<OperationalTeamDetailDto> CreateOperationalTeamAsync(
        CreateOperationalTeamRequest body,
        CancellationToken cancellationToken = default);

    Task<OperationalTeamDetailDto?> UpdateOperationalTeamAsync(
        string id,
        UpdateOperationalTeamRequest body,
        CancellationToken cancellationToken = default);

    Task<OperationalTeamDetailDto?> ReplaceOperationalTeamMembersAsync(
        string id,
        ReplaceOperationalTeamMembersRequest body,
        CancellationToken cancellationToken = default);

    Task<bool> AcknowledgeIntegrationAttentionIncidentAsync(
        string integrationId,
        string incidentId,
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator detail: integration, readiness, required slots, bindings, schedules (Epic 54).
    /// </summary>
    Task<IntegrationOperatorDetailDto?> GetIntegrationOperatorDetailAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists module procedures for an integration with server-owned launchability (ADR-0029, Epic 56A).
    /// </summary>
    Task<IntegrationProcedureListDto?> GetIntegrationProceduresAsync(string integrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Procedure definition for form rendering (ADR-0029, Epic 56A).
    /// </summary>
    Task<IntegrationProcedureDetailDto?> GetIntegrationProcedureAsync(string integrationId, string procedureId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Launches a procedure. On failure, <see cref="IntegrationProcedureLaunchClientResult"/> carries HTTP status and API <c>code</c>/<c>error</c> body when present.
    /// </summary>
    Task<IntegrationProcedureLaunchClientResult> LaunchIntegrationProcedureAsync(
        string integrationId,
        string procedureId,
        IReadOnlyDictionary<string, object?>? parameters,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an integration. Epic 49.
    /// </summary>
    Task<IntegrationDto> CreateIntegrationAsync(CreateIntegrationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an integration. Epic 49.
    /// </summary>
    Task<IntegrationDto> UpdateIntegrationAsync(string id, UpdateIntegrationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an integration. Epic 49.
    /// </summary>
    Task DeleteIntegrationAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates an integration for operational execution.
    /// </summary>
    Task<IntegrationDto> ActivateIntegrationAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates an integration while preserving configuration.
    /// </summary>
    Task<IntegrationDto> DeactivateIntegrationAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists bindings for an integration. Epic 50.
    /// </summary>
    Task<IReadOnlyList<IntegrationBindingDto>> GetIntegrationBindingsAsync(string integrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a binding for an integration. Epic 50.
    /// </summary>
    Task<IntegrationBindingDto> CreateIntegrationBindingAsync(string integrationId, CreateIntegrationBindingRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a binding from an integration. Epic 50.
    /// </summary>
    Task DeleteIntegrationBindingAsync(string integrationId, string bindingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets connections eligible for binding to a slot of an integration's module. Epic 50.
    /// </summary>
    Task<IReadOnlyList<EligibleConnectionDto>> GetIntegrationEligibleConnectionsAsync(string integrationId, string slotName, string contractType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets capability slots for an integration's module. Epic 50.
    /// </summary>
    Task<IReadOnlyList<ModuleSlotDto>> GetIntegrationModuleSlotsAsync(string integrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists schedules for an integration. Epic 50.
    /// </summary>
    Task<IReadOnlyList<ScheduleDto>> GetIntegrationSchedulesAsync(string integrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a schedule for an integration. Epic 50.
    /// </summary>
    Task<ScheduleDto> CreateIntegrationScheduleAsync(string integrationId, CreateIntegrationScheduleRequest request, CancellationToken cancellationToken = default);

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

    /// <summary>Merged sign-in capabilities and chooser policy (anonymous).</summary>
    Task<AuthSignInOptionsDto> GetAuthSignInOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets persisted auth UI policy. Returns null on 401/403.</summary>
    Task<AuthUiPolicyDto?> GetAuthUiPolicyAsync(CancellationToken cancellationToken = default);

    /// <summary>Updates auth UI policy. Throws <see cref="HttpRequestException"/> on 400/403/409.</summary>
    Task<AuthUiPolicyDto> PatchAuthUiPolicyAsync(UpdateAuthUiPolicyRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists platform settings. Returns null on 401/403.</summary>
    Task<IReadOnlyList<PlatformSettingResolvedDto>?> ListPlatformSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one platform setting. Returns null on 404/401/403.</summary>
    Task<PlatformSettingResolvedDto?> GetPlatformSettingAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Updates a platform setting. Throws on 400/403/409.</summary>
    Task<PlatformSettingResolvedDto> SetPlatformSettingAsync(string key, UpdatePlatformSettingRequest request, CancellationToken cancellationToken = default);

    /// <summary>Resets a platform setting DB override. Returns null on 404/401/403.</summary>
    Task<PlatformSettingResolvedDto?> ResetPlatformSettingAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads provider-backed platform setting options for a UI selector/editor.
    /// Returns null when unauthorized/forbidden/not found; throws on other non-success responses.
    /// </summary>
    Task<IReadOnlyList<PlatformSettingAllowedValueDto>?> GetPlatformSettingOptionsAsync(
        string key,
        string? integrationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists integration-scoped settings. Returns null on 401/403/404.</summary>
    Task<IReadOnlyList<PlatformSettingResolvedDto>?> ListIntegrationSettingsAsync(
        string integrationId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets one integration-scoped setting. Returns null on 404/401/403.</summary>
    Task<PlatformSettingResolvedDto?> GetIntegrationSettingAsync(
        string integrationId,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Updates an integration-scoped setting. Throws on 400/403/409.</summary>
    Task<PlatformSettingResolvedDto> SetIntegrationSettingAsync(
        string integrationId,
        string key,
        UpdatePlatformSettingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Resets an integration-scoped setting DB override. Returns null on 404/401/403.</summary>
    Task<PlatformSettingResolvedDto?> ResetIntegrationSettingAsync(
        string integrationId,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Loads provider-backed options for an integration-scoped setting.</summary>
    Task<IReadOnlyList<PlatformSettingAllowedValueDto>?> GetIntegrationSettingOptionsAsync(
        string integrationId,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Authentication overview for Config (Epic 44). Returns null on 401/403.</summary>
    Task<ConfigAuthOverviewDto?> GetConfigAuthOverviewAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists Entra registrations. Returns null on 401/403.</summary>
    Task<IReadOnlyList<EntraTenantListItemDto>?> GetEntraTenantsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one Entra registration. Returns null on 404/401/403.</summary>
    Task<EntraTenantListItemDto?> GetEntraTenantAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Creates an Entra registration. Throws on 400/403.</summary>
    Task<EntraTenantListItemDto> CreateEntraTenantAsync(CreateEntraTenantRequest request, CancellationToken cancellationToken = default);

    /// <summary>Updates an Entra registration. Throws on 400/403/409.</summary>
    Task<EntraTenantListItemDto> PatchEntraTenantAsync(string id, UpdateEntraTenantRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes an Entra registration. Throws on 400/403.</summary>
    Task DeleteEntraTenantAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Activates an Entra registration (clears other actives). Throws on 400/403/404.</summary>
    Task ActivateEntraTenantAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Validates OpenID metadata for an Entra registration. Throws on 403/404.</summary>
    Task<ValidateOpenIdResponseDto?> ValidateEntraTenantOpenIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Browses Entra directory users via Graph (discovery only).</summary>
    Task<EntraDirectoryUsersApiDto?> GetEntraDirectoryUsersAsync(string id, string? search = null, int top = 25, CancellationToken cancellationToken = default);

    /// <summary>Browses Entra directory groups via Graph (discovery only).</summary>
    Task<EntraDirectoryGroupsApiDto?> GetEntraDirectoryGroupsAsync(string id, string? search = null, int top = 25, CancellationToken cancellationToken = default);

    // --- Managed contacts (Epic 76) ---

    Task<IReadOnlyList<ManagedContactSummaryDto>?> ListManagedContactsAsync(string? query, int take = 50, CancellationToken cancellationToken = default);

    Task<ManagedContactDetailDto?> GetManagedContactAsync(string id, CancellationToken cancellationToken = default);

    Task<ManagedContactDetailDto?> CreateManagedContactAsync(CreateManagedContactRequest request, CancellationToken cancellationToken = default);

    Task<ManagedContactDetailDto?> UpdateManagedContactAsync(string id, UpdateManagedContactRequest request, CancellationToken cancellationToken = default);

    Task<ManagedContactDetailDto?> SetManagedContactEnabledAsync(string id, bool isEnabled, CancellationToken cancellationToken = default);

    Task<ImportEntraUsersResponseDto?> ImportEntraUsersToManagedContactsAsync(ImportEntraUsersRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ManagedContactGroupSummaryDto>?> ImportEntraGroupsMetadataAsync(ImportEntraGroupsRequest request, CancellationToken cancellationToken = default);

    Task<ManagedContactDetailDto?> LinkManagedContactToUserAsync(string id, LinkManagedContactUserRequest request, CancellationToken cancellationToken = default);

    Task<ManagedContactDetailDto?> UnlinkManagedContactUserAsync(string id, CancellationToken cancellationToken = default);

    // --- Notifications (Epic 77) ---

    Task<IReadOnlyList<NotificationDefinitionSummaryDto>> ListNotificationDefinitionsAsync(
        string? integrationId = null,
        string? eventType = null,
        bool? enabled = null,
        string? origin = null,
        int take = 100,
        CancellationToken cancellationToken = default);

    Task<NotificationDefinitionDetailDto?> GetNotificationDefinitionAsync(string id, CancellationToken cancellationToken = default);

    Task<NotificationDefinitionDetailDto?> SetNotificationDefinitionEnabledAsync(
        string id,
        bool isEnabled,
        CancellationToken cancellationToken = default);

    Task<RenderedNotificationSnapshotDto> RenderNotificationPreviewAsync(
        string definitionId,
        RenderNotificationPreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<RenderedNotificationSnapshotDto> RenderNotificationPreviewDraftAsync(
        RenderNotificationPreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlatformEventCatalogEntryDto>> ListPlatformEventCatalogAsync(
        CancellationToken cancellationToken = default);

    Task<PlatformEventCatalogEntryDto?> GetPlatformEventCatalogEntryAsync(
        string eventType,
        CancellationToken cancellationToken = default);

    Task<NotificationValidationResultDto> ValidateNotificationDefinitionAsync(
        ValidateNotificationDefinitionRequest request,
        CancellationToken cancellationToken = default);

    Task<NotificationDefinitionDetailDto> CreateNotificationDefinitionAsync(
        CreateNotificationDefinitionRequest request,
        CancellationToken cancellationToken = default);

    Task<NotificationDefinitionDetailDto?> UpdateNotificationDefinitionAsync(
        string id,
        UpdateNotificationDefinitionRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationSubscriptionDto>> ListNotificationSubscriptionsByDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationSubscriptionDto>> ListNotificationSubscriptionsByIntegrationAsync(
        string integrationId,
        string? definitionId = null,
        CancellationToken cancellationToken = default);

    Task<NotificationSubscriptionDto> AddNotificationSubscriptionAsync(
        string definitionId,
        AddNotificationSubscriptionRequest request,
        CancellationToken cancellationToken = default);

    Task<NotificationSubscriptionDto?> PatchNotificationSubscriptionAsync(
        string definitionId,
        string subscriptionId,
        PatchNotificationSubscriptionRequest request,
        CancellationToken cancellationToken = default);

    Task RemoveNotificationSubscriptionAsync(
        string definitionId,
        string subscriptionId,
        CancellationToken cancellationToken = default);

    Task<NotificationRecipientResolutionPreviewDto> ResolveNotificationRecipientsAsync(
        string definitionId,
        ResolveNotificationRecipientsRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationInstanceSummaryDto>> ListNotificationInstancesAsync(
        string? integrationId = null,
        string? definitionId = null,
        string? incidentId = null,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task<NotificationInstanceDetailDto?> GetNotificationInstanceAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationDeliveryOutboxDto>> ListNotificationInstanceOutboxAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PersonalNotificationInboxItemDto>> GetMyNotificationsAsync(
        string readFilter = "all",
        int take = 50,
        string? integrationId = null,
        string? kind = null,
        CancellationToken cancellationToken = default);

    Task<PersonalNotificationUnreadCountDto> GetMyUnreadNotificationCountAsync(CancellationToken cancellationToken = default);

    Task MarkMyNotificationReadAsync(string inboxItemId, CancellationToken cancellationToken = default);

    Task MarkMyNotificationUnreadAsync(string inboxItemId, CancellationToken cancellationToken = default);

    Task<PersonalNotificationMarkAllReadResultDto> MarkAllMyNotificationsReadAsync(CancellationToken cancellationToken = default);

    Task RecordMyNotificationsMenuViewedAsync(int take = 20, CancellationToken cancellationToken = default);

    Task RecordMyNotificationsPageViewedAsync(IReadOnlyList<string> inboxItemIds, CancellationToken cancellationToken = default);

    Task<NotificationDeliveryHealthSummaryDto> GetNotificationDeliveryHealthAsync(
        string? definitionId = null,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task<NotificationIntegrationDiagnosticsDto> GetNotificationIntegrationDiagnosticsAsync(
        string integrationId,
        string? definitionId = null,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task<NotificationIncidentDiagnosticsDto?> GetNotificationIncidentDiagnosticsAsync(
        string attentionIncidentId,
        string? definitionId = null,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task<ExpectedRunMissingParityDiagnosticsDto?> GetExpectedRunMissingParityAsync(
        string? integrationId = null,
        string? attentionIncidentId = null,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task<ExpectedRunMissingParityAcceptanceSummaryDto?> GetExpectedRunMissingParityAcceptanceAsync(
        string integrationId,
        string? attentionIncidentId = null,
        int maxIncidents = 10,
        int takePerIncident = 50,
        CancellationToken cancellationToken = default);

    Task<NotificationConfigurationDiagnosticsDto> GetNotificationConfigurationDiagnosticsAsync(
        CancellationToken cancellationToken = default);

    Task<ExpectedRunMissingCutoverSignOffDto?> GetExpectedRunMissingCutoverSignOffAsync(
        string integrationId,
        CancellationToken cancellationToken = default);

    Task<ExpectedRunMissingCutoverSignOffDto> CreateExpectedRunMissingCutoverSignOffAsync(
        string integrationId,
        CreateExpectedRunMissingCutoverSignOffRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ExpectedRunMissingCutoverSignOffDto> RevokeExpectedRunMissingCutoverSignOffAsync(
        string integrationId,
        RevokeExpectedRunMissingCutoverSignOffRequestDto? request = null,
        CancellationToken cancellationToken = default);

    Task<ExpectedRunMissingCutoverValidationDto> GetExpectedRunMissingCutoverValidationAsync(
        string integrationId,
        CancellationToken cancellationToken = default);

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
    bool CanAssignRoles = false,
    bool CanManageAuthUiPolicy = false,
    [property: JsonPropertyName("canViewAuthConfiguration")] bool CanViewAuthConfiguration = false,
    [property: JsonPropertyName("canManageEntraTenants")] bool CanManageEntraTenants = false,
    [property: JsonPropertyName("canViewConnections")] bool CanViewConnections = false,
    [property: JsonPropertyName("canViewIntegrations")] bool CanViewIntegrations = false,
    [property: JsonPropertyName("canManageIntegrations")] bool CanManageIntegrations = false,
    [property: JsonPropertyName("canViewConnectorDefinitions")] bool CanViewConnectorDefinitions = false,
    [property: JsonPropertyName("canCreateConnections")] bool CanCreateConnections = false,
    [property: JsonPropertyName("canUpdateConnections")] bool CanUpdateConnections = false,
    [property: JsonPropertyName("canRetireConnections")] bool CanRetireConnections = false,
    [property: JsonPropertyName("canViewManagedContacts")] bool CanViewManagedContacts = false,
    [property: JsonPropertyName("canManageManagedContacts")] bool CanManageManagedContacts = false,
    [property: JsonPropertyName("canImportManagedContacts")] bool CanImportManagedContacts = false,
    [property: JsonPropertyName("canViewNotificationDefinitions")] bool CanViewNotificationDefinitions = false,
    [property: JsonPropertyName("canManageNotificationDefinitions")] bool CanManageNotificationDefinitions = false,
    [property: JsonPropertyName("canUpdateIntegrations")] bool CanUpdateIntegrations = false,
    [property: JsonPropertyName("canViewPlatformSettings")] bool CanViewPlatformSettings = false,
    [property: JsonPropertyName("canManagePlatformSettings")] bool CanManagePlatformSettings = false);

public sealed record PlatformSettingAllowedValueDto(
    string Value,
    string Label,
    string? Description = null);

public sealed record PlatformSettingResolvedDto(
    string Key,
    string DisplayName,
    string Description,
    string Category,
    string ValueType,
    string InputKind,
    string? OptionProviderKey,
    string? OptionProviderContextKind,
    string CurrentValue,
    string Source,
    bool IsEditable,
    bool RequiresRestart,
    string? RowVersionBase64,
    int? MinInteger,
    int? MaxInteger,
    string? MinDurationIso,
    int? MinItems = null,
    int? MaxItems = null,
    IReadOnlyList<string>? CurrentValues = null,
    IReadOnlyList<PlatformSettingAllowedValueDto>? AllowedValues = null,
    string ValueScopeKind = "Instance",
    string? ValueScopeId = null);

public sealed record UpdatePlatformSettingRequest(
    string? Value = null,
    IReadOnlyList<string>? Values = null,
    string? RowVersionBase64 = null,
    string? IntegrationId = null);

public sealed record ManagedContactSummaryDto(
    string Id,
    string DisplayName,
    string? EmailAddress,
    string Source,
    bool IsEnabled,
    string UserLinkStatus,
    string NotificationUsabilityStatus,
    DateTimeOffset UpdatedAtUtc);

public sealed record ManagedContactIdentityLinkDto(
    string Id,
    string Provider,
    string ExternalObjectId,
    string? ExternalPrincipalName,
    DateTimeOffset? LastSyncedAtUtc,
    string LinkStatus,
    DateTimeOffset UpdatedAtUtc);

public sealed record ManagedContactDetailDto(
    string Id,
    string DisplayName,
    string? EmailAddress,
    string Source,
    bool IsEnabled,
    string? LinkedUserId,
    string UserLinkStatus,
    string NotificationUsabilityStatus,
    IReadOnlyList<ManagedContactIdentityLinkDto> IdentityLinks,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateManagedContactRequest(string DisplayName, string? EmailAddress, bool IsEnabled);

public sealed record UpdateManagedContactRequest(string DisplayName, string? EmailAddress, bool IsEnabled);

public sealed record ImportEntraUsersRequest(string EntraRegistrationId, IReadOnlyList<string> EntraUserObjectIds);

public sealed record ImportEntraUserItemResultDto(string EntraObjectId, bool Success, string? ManagedContactId, string OutcomeCode, string? ErrorMessage);

public sealed record ImportEntraUsersResponseDto(IReadOnlyList<ImportEntraUserItemResultDto> Items);

public sealed record ImportEntraGroupsRequest(string EntraRegistrationId, IReadOnlyList<string> EntraGroupObjectIds);

public sealed record ManagedContactGroupSummaryDto(string Id, string DisplayName, string EntraGroupObjectId, DateTimeOffset UpdatedAtUtc);

public sealed record LinkManagedContactUserRequest(string UserId);

public sealed record SetManagedContactEnabledRequest(bool IsEnabled);

/// <summary>Integration resource. Epic 49, Epic 50.</summary>
public sealed record IntegrationDto(
    string Id,
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    string State,
    IReadOnlyDictionary<string, string>? Settings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Request to create an integration. Epic 49, Epic 50.</summary>
public sealed record CreateIntegrationRequest(
    string DisplayName,
    string ModuleId,
    string? ScopeKey = null,
    IReadOnlyDictionary<string, string>? Settings = null);

/// <summary>Request to update an integration. Epic 49, Epic 50.</summary>
public sealed record UpdateIntegrationRequest(
    string? DisplayName = null,
    string? State = null,
    IReadOnlyDictionary<string, string>? Settings = null);

/// <summary>Binding owned by an Integration. Epic 50.</summary>
public sealed record IntegrationBindingDto(
    string Id,
    string? IntegrationId,
    string ModuleId,
    string SlotName,
    string ContractType,
    string? ConnectionId,
    string? CapabilityId,
    string State);

/// <summary>Request to create an integration binding. Epic 50.</summary>
public sealed record CreateIntegrationBindingRequest(
    string SlotName,
    string ContractType,
    string ConnectionId);

/// <summary>Connection eligible for binding. Epic 50.</summary>
public sealed record EligibleConnectionDto(string Id, string Name, string ConnectorDefinitionId);

/// <summary>Capability slot declared by a module. Epic 50.</summary>
public sealed record ModuleSlotDto(string ModuleId, string SlotName, string ContractType);

/// <summary>Where a connection is consumed (e.g. module slot binding). Epic 48, Epic 50.</summary>
public sealed record ConnectionUsedInEntry(
    string ModuleId,
    string SlotName,
    string ContractType,
    string? IntegrationId = null,
    string? IntegrationDisplayName = null);

/// <summary>Connection field shape for a connector definition (catalog). Epic 52.</summary>
public sealed record ConnectionFieldCatalogDto(
    string Key,
    string DisplayName,
    bool IsSecret,
    string? Description,
    bool IsRequired = false);

/// <summary>Connector definition catalog entry (shape of connections, not operations). Epic 52.</summary>
public sealed record ConnectorDefinitionCatalogDto(
    string Id,
    string Name,
    string? ModuleId,
    string? Description,
    IReadOnlyList<ConnectionFieldCatalogDto> Fields,
    string Capabilities);

/// <summary>Connection resource with Used In projection. Epic 48.</summary>
public sealed record ConnectionDto(
    string Id,
    string ConnectorDefinitionId,
    string Name,
    IReadOnlyDictionary<string, string> Config,
    IReadOnlyDictionary<string, string> SecretRefs,
    string? ScopeKey,
    string? OwnerId,
    string State,
    bool SupportsVerification,
    string LastVerificationStatus,
    DateTimeOffset? LastVerificationAtUtc,
    string? LastVerificationSummary,
    IReadOnlyList<ConnectionUsedInEntry> UsedIn);

/// <summary>Request to create a connection resource.</summary>
public sealed record CreateConnectionRequest(
    string ConnectorDefinitionId,
    string Name,
    IReadOnlyDictionary<string, string>? Config = null,
    IReadOnlyDictionary<string, string>? SecretRefs = null,
    string? ScopeKey = null,
    string? OwnerId = null,
    IReadOnlyDictionary<string, string>? SecretValues = null);

/// <summary>Request to update a connection resource.</summary>
public sealed record UpdateConnectionRequest(
    string? Name = null,
    IReadOnlyDictionary<string, string>? Config = null,
    IReadOnlyDictionary<string, string>? SecretRefs = null,
    IReadOnlyDictionary<string, string>? SecretValues = null);

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
    string ExecutionKind = "workflow",
    string? IntegrationId = null,
    string? ProcedureId = null);

public sealed record RunQueryFilter(
    string? ExecutionKind = null,
    string? OperationDefinitionId = null,
    string? Status = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    string? ModuleId = null,
    string? CorrelationKey = null,
    string? IntegrationId = null,
    int? Take = null);

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

/// <summary>Run detail from <c>GET /api/runs/{id}</c>. <see cref="StartParametersJson"/>, <see cref="IntegrationId"/>, and <see cref="ProcedureId"/> follow ADR-0031 (full start JSON for authorized readers; no field redaction today).</summary>
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
    RunForensicContextDto? ForensicContext = null,
    string? IntegrationId = null,
    string? ProcedureId = null);

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

public sealed record DashboardDefaultDto(
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("cards")] IReadOnlyList<DashboardCardDto> Cards,
    [property: JsonPropertyName("providerFailureCount")] int ProviderFailureCount = 0);

public sealed record DashboardCardDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("navigationHref")] string? NavigationHref,
    [property: JsonPropertyName("navigationLabel")] string? NavigationLabel,
    [property: JsonPropertyName("attention")] string Attention,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("facts")] IReadOnlyList<DashboardCardFactDto> Facts,
    [property: JsonPropertyName("isPlaceholder")] bool IsPlaceholder,
    [property: JsonPropertyName("placeholderReason")] string? PlaceholderReason);

public sealed record DashboardCardFactDto(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("value")] string Value);

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
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles,
    [property: JsonPropertyName("userPrincipal")] string? UserPrincipal);

public sealed record LocalLoginRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string? Password);

public sealed record LocalLoginResponse(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("displayName")] string DisplayName);

public sealed record AuthSignInOptionsDto(
    [property: JsonPropertyName("localSignInEnabled")] bool LocalSignInEnabled,
    [property: JsonPropertyName("entraSignInEnabled")] bool EntraSignInEnabled,
    [property: JsonPropertyName("entraMisconfigured")] bool EntraMisconfigured,
    [property: JsonPropertyName("showLocalSignInInPrimaryChooser")] bool ShowLocalSignInInPrimaryChooser,
    [property: JsonPropertyName("preferEntraInChooser")] bool PreferEntraInChooser,
    [property: JsonPropertyName("autoRedirectToEntra")] bool AutoRedirectToEntra,
    [property: JsonPropertyName("effectiveExperience")] string? EffectiveExperience = null,
    [property: JsonPropertyName("effectiveExperienceDescription")] string? EffectiveExperienceDescription = null);

public sealed record ConfigAuthOverviewDto(
    [property: JsonPropertyName("localAuthEnabled")] bool LocalAuthEnabled,
    [property: JsonPropertyName("entraEnabledInDeploymentConfiguration")] bool EntraEnabledInDeploymentConfiguration,
    [property: JsonPropertyName("entraConfigurationValid")] bool EntraConfigurationValid,
    [property: JsonPropertyName("entraMisconfiguredReason")] string? EntraMisconfiguredReason,
    [property: JsonPropertyName("activeEntraRegistrationId")] string? ActiveEntraRegistrationId,
    [property: JsonPropertyName("activeEntraRegistrationDisplayName")] string? ActiveEntraRegistrationDisplayName,
    [property: JsonPropertyName("entraSignInUsesActiveRegistration")] bool EntraSignInUsesActiveRegistration,
    [property: JsonPropertyName("runtimeMayRequireRestartToApplyChanges")] bool RuntimeMayRequireRestartToApplyChanges,
    [property: JsonPropertyName("runtimeApplicationStatus")] string RuntimeApplicationStatus,
    [property: JsonPropertyName("showLocalSignInInPrimaryChooser")] bool ShowLocalSignInInPrimaryChooser,
    [property: JsonPropertyName("preferEntraInChooser")] bool PreferEntraInChooser,
    [property: JsonPropertyName("autoRedirectToEntra")] bool AutoRedirectToEntra,
    [property: JsonPropertyName("localShownOnPrimaryLogin")] bool LocalShownOnPrimaryLogin,
    [property: JsonPropertyName("localFallbackOnly")] bool LocalFallbackOnly,
    [property: JsonPropertyName("entraSignInEnabled")] bool EntraSignInEnabled,
    [property: JsonPropertyName("effectiveExperience")] string EffectiveExperience,
    [property: JsonPropertyName("effectiveExperienceDescription")] string EffectiveExperienceDescription,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("authProviderAvailability")] AuthProviderAvailabilityDto? AuthProviderAvailability = null);

public sealed record AuthProviderAvailabilityDto(
    [property: JsonPropertyName("entraSignInReady")] bool EntraSignInReady,
    [property: JsonPropertyName("localSignInAvailable")] bool LocalSignInAvailable,
    [property: JsonPropertyName("activeEntraOpenIdConfigurationStatus")] string? ActiveEntraOpenIdConfigurationStatus,
    [property: JsonPropertyName("activeEntraDirectoryDiscoveryStatus")] string? ActiveEntraDirectoryDiscoveryStatus,
    [property: JsonPropertyName("adminDiagnosis")] string? AdminDiagnosis);

public sealed record EntraTenantListItemDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("authority")] string Authority,
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("usesDeploymentSecret")] bool UsesDeploymentSecret,
    [property: JsonPropertyName("callbackPath")] string? CallbackPath,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("isActive")] bool IsActive,
    [property: JsonPropertyName("deploymentSecretConfigured")] bool DeploymentSecretConfigured,
    [property: JsonPropertyName("azureTenantId")] string? AzureTenantId = null,
    [property: JsonPropertyName("openIdConfigurationStatus")] string? OpenIdConfigurationStatus = null,
    [property: JsonPropertyName("lastOpenIdValidationUtc")] DateTimeOffset? LastOpenIdValidationUtc = null,
    [property: JsonPropertyName("lastOpenIdValidationSummary")] string? LastOpenIdValidationSummary = null,
    [property: JsonPropertyName("lastOpenIdValidationDetail")] string? LastOpenIdValidationDetail = null,
    [property: JsonPropertyName("directoryDiscoveryStatus")] string? DirectoryDiscoveryStatus = null,
    [property: JsonPropertyName("lastDirectoryDiscoveryUtc")] DateTimeOffset? LastDirectoryDiscoveryUtc = null,
    [property: JsonPropertyName("lastDirectoryDiscoverySummary")] string? LastDirectoryDiscoverySummary = null,
    [property: JsonPropertyName("lastDirectoryDiscoveryDetail")] string? LastDirectoryDiscoveryDetail = null,
    [property: JsonPropertyName("rowVersionBase64")] string? RowVersionBase64 = null);

public sealed record CreateEntraTenantRequest(
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("authority")] string Authority,
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("usesDeploymentSecret")] bool UsesDeploymentSecret = true,
    [property: JsonPropertyName("callbackPath")] string? CallbackPath = null,
    [property: JsonPropertyName("enabled")] bool Enabled = true,
    [property: JsonPropertyName("azureTenantId")] string? AzureTenantId = null);

public sealed record UpdateEntraTenantRequest(
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("authority")] string Authority,
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("usesDeploymentSecret")] bool UsesDeploymentSecret,
    [property: JsonPropertyName("callbackPath")] string? CallbackPath,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("rowVersionBase64")] string? RowVersionBase64,
    [property: JsonPropertyName("azureTenantId")] string? AzureTenantId = null);

public sealed record ValidationResultDto(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("openIdConfigurationStatus")] string OpenIdConfigurationStatus,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("technicalDetail")] string? TechnicalDetail);

public sealed record ValidateOpenIdResponseDto(
    [property: JsonPropertyName("validation")] ValidationResultDto Validation,
    [property: JsonPropertyName("tenant")] EntraTenantListItemDto Tenant);

public sealed record EntraDirectoryUserItemDto(
    [property: JsonPropertyName("objectId")] string ObjectId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("userPrincipalName")] string? UserPrincipalName,
    [property: JsonPropertyName("mail")] string? Mail);

public sealed record EntraDirectoryGroupItemDto(
    [property: JsonPropertyName("objectId")] string ObjectId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("mailNickname")] string? MailNickname,
    [property: JsonPropertyName("description")] string? Description);

public sealed record EntraDirectoryUsersApiDto(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("failureCategory")] string? FailureCategory,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("technicalDetail")] string? TechnicalDetail,
    [property: JsonPropertyName("items")] IReadOnlyList<EntraDirectoryUserItemDto> Items);

public sealed record EntraDirectoryGroupsApiDto(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("failureCategory")] string? FailureCategory,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("technicalDetail")] string? TechnicalDetail,
    [property: JsonPropertyName("items")] IReadOnlyList<EntraDirectoryGroupItemDto> Items);

public sealed record AuthUiPolicyDto(
    [property: JsonPropertyName("showLocalSignInInPrimaryChooser")] bool ShowLocalSignInInPrimaryChooser,
    [property: JsonPropertyName("preferEntraInChooser")] bool PreferEntraInChooser,
    [property: JsonPropertyName("autoRedirectToEntra")] bool AutoRedirectToEntra,
    [property: JsonPropertyName("rowVersionBase64")] string? RowVersionBase64);

public sealed record UpdateAuthUiPolicyRequest(
    [property: JsonPropertyName("showLocalSignInInPrimaryChooser")] bool ShowLocalSignInInPrimaryChooser,
    [property: JsonPropertyName("preferEntraInChooser")] bool PreferEntraInChooser,
    [property: JsonPropertyName("autoRedirectToEntra")] bool AutoRedirectToEntra,
    [property: JsonPropertyName("rowVersionBase64")] string? RowVersionBase64);
