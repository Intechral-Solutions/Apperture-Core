using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Registered mapping for gateway result persistence to platform-managed SQL.
/// Governed; prevents arbitrary table writes by gateway modules.
/// </summary>
public sealed record MappingDefinition(
    MappingDefinitionId Id,
    string Name,
    string? ModuleId,
    string SourceType,
    string TargetSchema,
    string TargetTable,
    IReadOnlyDictionary<string, string> ColumnMappings) : IGovernedResource
{
    /// <inheritdoc />
    public IGovernedResourceIdentity GetIdentity() =>
        new Governance.GovernedResourceIdentity(
            ResourceTypeConstants.MappingDefinition,
            Id.Value,
            ModuleId is not null ? $"module:{ModuleId}" : null,
            null);
}
