using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Registers and resolves mapping definitions for gateway result persistence.
/// Prevents arbitrary table writes; gateway modules use registered mappings only.
/// </summary>
public interface IMappingDefinitionRegistry
{
    /// <summary>
    /// Registers a mapping definition.
    /// </summary>
    void Register(MappingDefinition definition);

    /// <summary>
    /// Gets a mapping definition by id.
    /// </summary>
    MappingDefinition? Get(MappingDefinitionId id);

    /// <summary>
    /// Gets all registered mapping definitions, optionally filtered by module.
    /// </summary>
    IReadOnlyList<MappingDefinition> GetAll(string? moduleId = null);
}
