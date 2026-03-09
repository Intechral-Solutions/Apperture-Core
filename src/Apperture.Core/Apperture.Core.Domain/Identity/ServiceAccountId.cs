using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Unique identifier for a ServiceAccount (machine/environment identity).
/// </summary>
public sealed record ServiceAccountId(string Value) : EntityId(Value);
