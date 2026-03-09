using Apperture.Core.Domain.Environment;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Operations;

/// <summary>
/// Single run of an operation/workflow. Entity or aggregate.
/// </summary>
public sealed record ExecutionRecord(
    ExecutionId Id,
    OperationId OperationId,
    ModuleInstanceId? ModuleInstanceId,
    ExecutionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    ActorId ActorId,
    CorrelationId CorrelationId,
    string? Outcome);
