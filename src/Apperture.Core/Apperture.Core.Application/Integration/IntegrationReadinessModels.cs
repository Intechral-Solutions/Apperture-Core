namespace Apperture.Core.Application.Integration;

/// <summary>Stable codes for readiness issues (API and tests).</summary>
public static class IntegrationReadinessIssueCodes
{
    public const string IntegrationNotActive = "integration_not_active";
    public const string ModuleNotInstalled = "module_not_installed";
    public const string MissingSetting = "missing_setting";
    public const string InvalidSetting = "invalid_setting";
    public const string MissingSecret = "missing_secret";
    public const string InvalidSecretReference = "invalid_secret_reference";
    public const string MissingSlotBinding = "missing_slot_binding";
    public const string DuplicateActiveBindingForSlot = "duplicate_active_binding_for_slot";
    public const string InactiveBinding = "inactive_binding";
    public const string BindingContractMismatch = "binding_contract_mismatch";
    public const string BindingMissingConnectionOrCapability = "binding_missing_connection_or_capability";
    public const string ConnectionMissing = "connection_missing";
    public const string ConnectionInactive = "connection_inactive";
    public const string BindingNotEligible = "binding_not_eligible";
    public const string ScheduleOwnershipMismatch = "schedule_ownership_mismatch";
    public const string ConnectionVerificationFailed = "connection_verification_failed";
    public const string ConnectionVerificationUnverified = "connection_unverified";
    public const string ConnectionVerificationUnavailable = "connection_verification_unavailable";
    public const string OptionalDependencyUnavailable = "optional_dependency_unavailable";
    public const string ModuleCheckFailed = "module_check_failed";
}

/// <summary>Evidence-based readiness projection for an integration.</summary>
public sealed record IntegrationReadinessProjection(
    string IntegrationId,
    string ModuleId,
    bool IsRunnable,
    int BlockingIssueCount,
    int AdvisoryIssueCount,
    IReadOnlyList<IntegrationReadinessIssue> Issues);

public sealed record IntegrationReadinessIssue(
    string Code,
    string Message,
    string? SlotName,
    string? BindingId,
    string? ConnectionId,
    string Severity = "Blocking");

public enum IntegrationReadinessIssueSeverity
{
    Blocking = 0,
    Advisory = 1
}

public sealed record IntegrationModuleSlotInfo(string SlotName, string ContractType);

public sealed record IntegrationBindingSummary(
    string Id,
    string SlotName,
    string ContractType,
    string? ConnectionId,
    string? CapabilityId,
    string State,
    string? ConnectionVerificationStatus = null,
    DateTimeOffset? ConnectionVerifiedAtUtc = null);

public sealed record IntegrationScheduleSummary(
    string Id,
    string Name,
    bool Enabled,
    string RecurrenceExpression,
    string? IntegrationId);

/// <summary>Operator-facing single view of an integration (control plane).</summary>
public sealed record IntegrationOperatorDetailProjection(
    string Id,
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    string State,
    IReadOnlyDictionary<string, string>? Settings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IntegrationReadinessProjection Readiness,
    IReadOnlyList<IntegrationModuleSlotInfo> RequiredSlots,
    IReadOnlyList<IntegrationBindingSummary> Bindings,
    IReadOnlyList<IntegrationScheduleSummary> Schedules,
    /// <summary>CTX-1: Integration's current owning ResourceContext id. Backfilled here (CTX-3A Plan 4
    /// Task 7) -- the domain entity has carried this since CTX-1, but this operator-detail projection
    /// was silently dropping it, same latent gap ScheduleDto/ScheduleDetailDto had before their CTX-1
    /// backfill.</summary>
    string? ResourceContextId = null);
