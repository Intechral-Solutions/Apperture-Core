using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Audit;

/// <summary>
/// Unique identifier for an Audit Record.
/// </summary>
public sealed record AuditRecordId(string Value) : EntityId(Value);
