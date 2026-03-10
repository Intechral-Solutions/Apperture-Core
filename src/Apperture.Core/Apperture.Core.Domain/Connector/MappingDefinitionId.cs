using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Unique identifier for a gateway result persistence mapping definition.
/// </summary>
public sealed record MappingDefinitionId(string Value) : EntityId(Value);
