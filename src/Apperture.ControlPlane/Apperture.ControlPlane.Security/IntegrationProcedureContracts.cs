namespace Apperture.ControlPlane.Security;

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

public sealed record IntegrationProcedureSummaryDto(
    string Id,
    string DisplayName,
    string? Description,
    string? Category,
    string LaunchKind,
    string TargetDefinitionId);

public sealed record IntegrationProcedureDetailDto(
    string Id,
    string DisplayName,
    string? Description,
    string? Category,
    string LaunchKind,
    string TargetDefinitionId,
    IReadOnlyList<IntegrationProcedureParameterFieldDto> ParameterFields);

public sealed record IntegrationProcedureListResponse(
    bool IntegrationReady,
    IReadOnlyList<IntegrationProcedureSummaryDto> Procedures);

public sealed record IntegrationProcedureLaunchRequest(
    IReadOnlyDictionary<string, object?>? Parameters,
    string? IdempotencyKey,
    string? CorrelationId);

public sealed record IntegrationProcedureLaunchResponse(
    string RunId,
    string? WorkflowInstanceId,
    string? OperationInstanceId,
    string ExecutionDefinitionId,
    string Status,
    string LaunchKind);

/// <summary>
/// Integration-scoped procedure listing, detail, and launch (ADR-0029).
/// </summary>
public interface IIntegrationProcedureService
{
    ValueTask<IntegrationProcedureListResponse?> ListAvailableAsync(string integrationId, CancellationToken cancellationToken = default);

    ValueTask<IntegrationProcedureDetailDto?> GetProcedureAsync(string integrationId, string procedureId, CancellationToken cancellationToken = default);

    ValueTask<IntegrationProcedureLaunchResponse?> LaunchAsync(
        string integrationId,
        string procedureId,
        IntegrationProcedureLaunchRequest request,
        CancellationToken cancellationToken = default);
}
