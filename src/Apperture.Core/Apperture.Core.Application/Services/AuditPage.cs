using Apperture.Core.Domain.Audit;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Paginated result of audit records.
/// </summary>
public sealed record AuditPage(
    IReadOnlyList<AuditRecord> Items,
    string? NextContinuationToken);
