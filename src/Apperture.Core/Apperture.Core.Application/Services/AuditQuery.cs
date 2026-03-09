using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Query parameters for audit record retrieval.
/// </summary>
public sealed record AuditQuery(
    ActorId? FilterByActorId = null,
    ResourceDescriptor? Resource = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int PageSize = 50,
    string? ContinuationToken = null);
