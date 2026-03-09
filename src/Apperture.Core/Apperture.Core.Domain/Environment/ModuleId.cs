using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Environment;

/// <summary>
/// Unique identifier for a Module (installable capability type).
/// </summary>
public sealed record ModuleId(string Value) : EntityId(Value);
