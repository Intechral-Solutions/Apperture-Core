using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Unique identifier for a User (human operator).
/// </summary>
public sealed record UserId(string Value) : EntityId(Value);
