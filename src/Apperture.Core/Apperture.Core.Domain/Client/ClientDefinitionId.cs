using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Client;

/// <summary>
/// Unique identifier for a client definition. Epic 51.
/// </summary>
public sealed record ClientDefinitionId(string Value) : EntityId(Value);
