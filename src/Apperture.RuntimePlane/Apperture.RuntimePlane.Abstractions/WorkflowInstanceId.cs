using Apperture.Shared.Kernel;

namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// Unique identifier for a workflow instance (single run) in the runtime plane.
/// </summary>
public sealed record WorkflowInstanceId(string Value) : EntityId(Value);
