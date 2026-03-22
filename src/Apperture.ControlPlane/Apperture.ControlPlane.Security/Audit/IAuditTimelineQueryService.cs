using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security.Audit;

/// <summary>
/// Query service for audit timeline discoverability. Epic 32.
/// </summary>
public interface IAuditTimelineQueryService
{
    /// <summary>
    /// Queries the audit timeline with the given filters.
    /// </summary>
    ValueTask<AuditTimelinePage> QueryAsync(AuditFilterCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single audit entry by id, or null if not found.
    /// </summary>
    ValueTask<AuditTimelineEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
}
