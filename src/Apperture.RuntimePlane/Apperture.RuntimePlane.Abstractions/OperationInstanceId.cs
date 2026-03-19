using Apperture.Shared.Kernel;

namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// Unique identifier for an operation instance (single execution) in the runtime plane.
/// </summary>
public sealed record OperationInstanceId(string Value) : EntityId(Value);

