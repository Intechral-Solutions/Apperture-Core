using Apperture.Shared.Kernel;

namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// Unique identifier for an operation run (top-level execution record) in the runtime plane.
/// </summary>
public sealed record OperationRunId(string Value) : EntityId(Value);
