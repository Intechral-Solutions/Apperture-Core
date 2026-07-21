namespace Apperture.Management.Api.Models;

public sealed record IntegrationConfigurationDeclarationsDto(
    string ModuleId,
    IReadOnlyList<IntegrationSettingDeclarationDto> Settings,
    IReadOnlyList<IntegrationConnectionRequirementDto> Connections,
    IReadOnlyList<IntegrationProcedureRequirementDto> Procedures);

public sealed record IntegrationSettingDeclarationDto(
    string Key,
    string DisplayName,
    string Description,
    string ValueKind,
    bool IsRequired,
    bool IsSensitive,
    int Order,
    string? Group);

public sealed record IntegrationConnectionRequirementDto(
    string Key,
    string DisplayName,
    string Description,
    bool IsRequired,
    string? CapabilityKind,
    string CapabilityLabel,
    string TechnicalContractType,
    string VerificationExpectation,
    bool AllowMultipleBindings);

public sealed record IntegrationProcedureRequirementDto(
    string ProcedureId,
    IReadOnlyList<string> RequiredSettingKeys,
    IReadOnlyList<string> RequiredConnectionKeys);

public sealed record IntegrationProcedureReadinessDto(
    bool IsRunnable,
    int BlockingIssueCount,
    int AdvisoryIssueCount,
    IReadOnlyList<IntegrationReadinessIssueDto> Issues);

public sealed record IntegrationReadinessIssueDto(
    string Code,
    string Message,
    string? SlotName,
    string? BindingId,
    string? ConnectionId,
    string Severity);
