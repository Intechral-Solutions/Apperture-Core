using Apperture.Core.Domain.Operations;

namespace Apperture.ControlPlane.Security;

public interface IOperationDefinitionQueryService
{
    ValueTask<IReadOnlyList<OperationDefinitionDto>> ListAsync(CancellationToken cancellationToken = default);
    ValueTask<OperationDefinitionDto?> GetAsync(OperationId operationDefinitionId, CancellationToken cancellationToken = default);
}

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

