using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Unique identifier for a binding (slot to connection/capability delegation).
/// </summary>
public sealed record BindingId(string Value) : EntityId(Value);
