namespace Apperture.Management.Client;

/// <summary>SDK-6C/6D: module-authored configuration declarations for an integration's module.</summary>
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

/// <summary>Operator-facing connection requirement card (SDK-6D).</summary>
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

/// <summary>Procedure-scoped readiness from GET integrations/{id}/procedures/{procedureId}/readiness.</summary>
public sealed record IntegrationProcedureReadinessDto(
    bool IsRunnable,
    int BlockingIssueCount,
    int AdvisoryIssueCount,
    IReadOnlyList<IntegrationReadinessIssueDto> Issues);
