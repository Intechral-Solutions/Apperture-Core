namespace Apperture.Management.Client;

/// <summary>ADR-0029: parameter field metadata for procedure forms.</summary>
public sealed record IntegrationProcedureParameterFieldDto(
    string Key,
    string Type,
    bool Required,
    IReadOnlyList<string>? AllowedValues,
    decimal? Min,
    decimal? Max,
    int? MaxLength,
    string? Help,
    string? Label,
    string? DefaultJson);

/// <summary>Epic 56A: server-owned launchability; <see cref="BlockedReasonCodes"/> empty when <see cref="Launchable"/>.</summary>
public sealed record IntegrationProcedureItemDto(
    string Id,
    string DisplayName,
    string? Description,
    string? Category,
    string LaunchKind,
    string TargetDefinitionId,
    bool Launchable,
    IReadOnlyList<string> BlockedReasonCodes);

public sealed record IntegrationProcedureListDto(bool IntegrationReady, IReadOnlyList<IntegrationProcedureItemDto> Procedures);

public sealed record IntegrationProcedureDetailDto(
    string Id,
    string DisplayName,
    string? Description,
    string? Category,
    string LaunchKind,
    string TargetDefinitionId,
    bool Launchable,
    IReadOnlyList<string> BlockedReasonCodes,
    IReadOnlyList<IntegrationProcedureParameterFieldDto> ParameterFields);

public sealed record IntegrationProcedureLaunchResultDto(
    string RunId,
    string? WorkflowInstanceId,
    string? OperationInstanceId,
    string ExecutionDefinitionId,
    string Status,
    string LaunchKind);

/// <summary>Outcome of procedure launch including API error body when unsuccessful (Epic 56A).</summary>
public sealed record IntegrationProcedureLaunchClientResult(
    bool Succeeded,
    IntegrationProcedureLaunchResultDto? Result,
    int StatusCode,
    string? ErrorCode,
    string? ErrorMessage);
