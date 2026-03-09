using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Operations;

/// <summary>
/// Unique identifier for an Operation Definition.
/// </summary>
public sealed record OperationId(string Value) : EntityId(Value);
