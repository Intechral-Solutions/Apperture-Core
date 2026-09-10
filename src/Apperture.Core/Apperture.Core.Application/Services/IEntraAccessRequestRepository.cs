using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

public interface IEntraAccessRequestRepository
{
    ValueTask<EntraAccessRequest?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    ValueTask<EntraAccessRequest?> FindPendingAsync(
        string tenantId,
        string objectId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<EntraAccessRequest>> FindPendingByObjectIdsAsync(
        string tenantId,
        IReadOnlyList<string> objectIds,
        CancellationToken cancellationToken = default);

    ValueTask<EntraAccessRequest?> FindLatestAsync(
        string tenantId,
        string objectId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<EntraAccessRequest>> ListPendingAsync(CancellationToken cancellationToken = default);

    ValueTask<int> CountPendingAsync(CancellationToken cancellationToken = default);

    ValueTask<int> CountRecentForIdentityAsync(
        string tenantId,
        string objectId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default);

    ValueTask AddAsync(EntraAccessRequest request, CancellationToken cancellationToken = default);

    ValueTask UpdateAsync(EntraAccessRequest request, CancellationToken cancellationToken = default);
}
