using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Environment;

/// <summary>
/// Unique identifier for a Runtime Plane (execution/data-plane subsystem within an Environment).
/// </summary>
public sealed record RuntimePlaneId(string Value) : EntityId(Value);
