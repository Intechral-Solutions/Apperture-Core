using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Operations;

/// <summary>
/// Unique identifier for an Execution Record (single run).
/// </summary>
public sealed record ExecutionId(string Value) : EntityId(Value);
