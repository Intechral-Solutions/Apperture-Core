using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Unique identifier for a Role.
/// </summary>
public sealed record RoleId(string Value) : EntityId(Value);
