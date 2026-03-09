using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Environment;

/// <summary>
/// Unique identifier for an Environment (full deployed platform boundary for a tenant/org).
/// </summary>
public sealed record EnvironmentId(string Value) : EntityId(Value);
