using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Environment;

/// <summary>
/// Unique identifier for a Module Installation (module enabled in an Environment).
/// </summary>
public sealed record ModuleInstallationId(string Value) : EntityId(Value);
