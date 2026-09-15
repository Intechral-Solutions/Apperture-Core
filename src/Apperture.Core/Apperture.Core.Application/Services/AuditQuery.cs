using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Query parameters for audit record retrieval.
/// Epic 37: FilterByOutcome, FilterByModuleId, FilterByScheduleId, FilterByRunId are DB-backed.
/// CTX-2: FilterByResourceContextId is DB-backed.
/// </summary>
public sealed record AuditQuery(
    ActorId? FilterByActorId = null,
    ResourceDescriptor? Resource = null,
    string? FilterByActionPrefix = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int PageSize = 50,
    string? ContinuationToken = null,
    string? FilterByOutcome = null,
    string? FilterByModuleId = null,
    string? FilterByScheduleId = null,
    string? FilterByRunId = null,
    string? FilterByResourceContextId = null,
    AuditContextClassificationFilter FilterByContextClassification = AuditContextClassificationFilter.None);
