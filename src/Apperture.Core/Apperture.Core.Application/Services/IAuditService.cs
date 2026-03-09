using Apperture.Core.Domain.Audit;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Appends and queries audit records. Sink in Kernel; persistence in infrastructure.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Appends an audit record.
    /// </summary>
    ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries audit records with optional filters.
    /// </summary>
    ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);
}
