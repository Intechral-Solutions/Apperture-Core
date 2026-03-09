using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Environment;

/// <summary>
/// Unique identifier for a Module Instance (configured executable occurrence of a Module Installation).
/// </summary>
public sealed record ModuleInstanceId(string Value) : EntityId(Value);
