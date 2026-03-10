using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Unique identifier for a connection resource.
/// </summary>
public sealed record ConnectionId(string Value) : EntityId(Value);
