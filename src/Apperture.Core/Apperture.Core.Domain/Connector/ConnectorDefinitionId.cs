using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Unique identifier for a connector definition.
/// </summary>
public sealed record ConnectorDefinitionId(string Value) : EntityId(Value);
