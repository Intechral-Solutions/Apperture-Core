namespace Apperture.Management.Client;

/// <summary>Epic 54: evidence-based readiness from GET integrations/{id}/detail.</summary>
public sealed record IntegrationReadinessIssueDto(
    string Code,
    string Message,
    string? SlotName,
    string? BindingId,
    string? ConnectionId,
    string Severity = "Blocking");

public sealed record IntegrationReadinessDto(
    string IntegrationId,
    string ModuleId,
    bool IsRunnable,
    IntegrationReadinessIssueDto[] Issues);

public sealed record IntegrationModuleSlotSummaryDto(string SlotName, string ContractType);

public sealed record IntegrationBindingSummaryDto(
    string Id,
    string SlotName,
    string ContractType,
    string? ConnectionId,
    string? CapabilityId,
    string State,
    string? ConnectionVerificationStatus,
    DateTimeOffset? ConnectionVerifiedAtUtc);

public sealed record IntegrationScheduleSummaryDto(
    string Id,
    string Name,
    bool Enabled,
    string RecurrenceExpression,
    string? IntegrationId);

/// <summary>Single operator view: integration + readiness + slots + bindings + schedules.</summary>
public sealed record IntegrationOperatorDetailDto(
    string Id,
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    string State,
    IReadOnlyDictionary<string, string>? Settings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IntegrationReadinessDto Readiness,
    IntegrationModuleSlotSummaryDto[] RequiredSlots,
    IntegrationBindingSummaryDto[] Bindings,
    IntegrationScheduleSummaryDto[] Schedules);
