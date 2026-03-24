namespace Apperture.Core.Application.Integration;

/// <summary>Stable codes for readiness issues (API and tests).</summary>
public static class IntegrationReadinessIssueCodes
{
    public const string IntegrationNotActive = "integration_not_active";
    public const string MissingSlotBinding = "missing_slot_binding";
    public const string DuplicateActiveBindingForSlot = "duplicate_active_binding_for_slot";
    public const string InactiveBinding = "inactive_binding";
    public const string BindingContractMismatch = "binding_contract_mismatch";
    public const string BindingMissingConnectionOrCapability = "binding_missing_connection_or_capability";
    public const string ConnectionMissing = "connection_missing";
    public const string ConnectionInactive = "connection_inactive";
    public const string BindingNotEligible = "binding_not_eligible";
    public const string ScheduleOwnershipMismatch = "schedule_ownership_mismatch";
}

/// <summary>Evidence-based readiness projection for an integration.</summary>
public sealed record IntegrationReadinessProjection(
    string IntegrationId,
    string ModuleId,
    bool IsRunnable,
    IReadOnlyList<IntegrationReadinessIssue> Issues);

public sealed record IntegrationReadinessIssue(
    string Code,
    string Message,
    string? SlotName,
    string? BindingId,
    string? ConnectionId);

public sealed record IntegrationModuleSlotInfo(string SlotName, string ContractType);

public sealed record IntegrationBindingSummary(
    string Id,
    string SlotName,
    string ContractType,
    string? ConnectionId,
    string? CapabilityId,
    string State);

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
    IReadOnlyList<IntegrationScheduleSummary> Schedules);
