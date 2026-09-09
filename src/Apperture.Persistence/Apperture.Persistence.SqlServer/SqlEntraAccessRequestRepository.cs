using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlEntraAccessRequestRepository : IEntraAccessRequestRepository
{
    private readonly AppertureDbContext _db;

    public SqlEntraAccessRequestRepository(AppertureDbContext db) => _db = db;

    public async ValueTask<EntraAccessRequest?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var e = await _db.EntraAccessRequests.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return e is null ? null : ToDomain(e);
    }

    public async ValueTask<EntraAccessRequest?> FindPendingAsync(
        string registrationId,
        string tenantId,
        string objectId,
        CancellationToken cancellationToken = default)
    {
        var e = await _db.EntraAccessRequests.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.RegistrationId == registrationId
                    && x.TenantId == tenantId
                    && x.ProviderObjectId == objectId
                    && x.Status == (int)EntraAccessRequestStatus.Pending,
                cancellationToken);
        return e is null ? null : ToDomain(e);
    }

    public async ValueTask<IReadOnlyList<EntraAccessRequest>> FindPendingByObjectIdsAsync(
        string registrationId,
        IReadOnlyList<string> objectIds,
        CancellationToken cancellationToken = default)
    {
        if (objectIds.Count == 0)
            return [];

        var ids = objectIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rows = await _db.EntraAccessRequests.AsNoTracking()
            .Where(x => x.RegistrationId == registrationId
                && x.Status == (int)EntraAccessRequestStatus.Pending
                && ids.Contains(x.ProviderObjectId))
            .ToListAsync(cancellationToken);
        return rows.ConvertAll(ToDomain);
    }

    public async ValueTask<EntraAccessRequest?> FindLatestAsync(
        string registrationId,
        string tenantId,
        string objectId,
        CancellationToken cancellationToken = default)
    {
        var e = await _db.EntraAccessRequests.AsNoTracking()
            .Where(x => x.RegistrationId == registrationId
                && x.TenantId == tenantId
                && x.ProviderObjectId == objectId)
            .OrderByDescending(x => x.RequestedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return e is null ? null : ToDomain(e);
    }

    public async ValueTask<IReadOnlyList<EntraAccessRequest>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.EntraAccessRequests.AsNoTracking()
            .Where(x => x.Status == (int)EntraAccessRequestStatus.Pending)
            .OrderBy(x => x.RequestedAtUtc)
            .ToListAsync(cancellationToken);
        return rows.ConvertAll(ToDomain);
    }

    public async ValueTask<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
        await _db.EntraAccessRequests.CountAsync(x => x.Status == (int)EntraAccessRequestStatus.Pending, cancellationToken);

    public async ValueTask<int> CountRecentForIdentityAsync(
        string registrationId,
        string tenantId,
        string objectId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default) =>
        await _db.EntraAccessRequests.CountAsync(
            x => x.RegistrationId == registrationId
                && x.TenantId == tenantId
                && x.ProviderObjectId == objectId
                && x.RequestedAtUtc >= sinceUtc,
            cancellationToken);

    public async ValueTask AddAsync(EntraAccessRequest request, CancellationToken cancellationToken = default)
    {
        _db.EntraAccessRequests.Add(ToEntity(request));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask UpdateAsync(EntraAccessRequest request, CancellationToken cancellationToken = default)
    {
        var e = await _db.EntraAccessRequests
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Access request '{request.Id}' was not found.");
        e.Status = (int)request.Status;
        e.ReviewedAtUtc = request.ReviewedAtUtc;
        e.ReviewedByUserId = request.ReviewedByUserId;
        e.ProvisionedUserId = request.ProvisionedUserId;
        e.DisplayNameSnapshot = request.DisplayNameSnapshot;
        e.UserPrincipalNameSnapshot = request.UserPrincipalNameSnapshot;
        e.MailSnapshot = request.MailSnapshot;
        e.UserTypeSnapshot = request.UserTypeSnapshot;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static EntraAccessRequest ToDomain(EntraAccessRequestEntity e) =>
        new(
            e.Id,
            e.RegistrationId,
            e.TenantId,
            e.ProviderObjectId,
            e.DisplayNameSnapshot,
            e.UserPrincipalNameSnapshot,
            e.MailSnapshot,
            e.UserTypeSnapshot,
            e.AuthenticatedAtUtc,
            e.RequestedAtUtc,
            (EntraAccessRequestStatus)e.Status,
            e.ReviewedAtUtc,
            e.ReviewedByUserId,
            e.ProvisionedUserId);

    private static EntraAccessRequestEntity ToEntity(EntraAccessRequest request) =>
        new()
        {
            Id = request.Id,
            RegistrationId = request.RegistrationId,
            TenantId = request.TenantId,
            ProviderObjectId = request.ProviderObjectId,
            DisplayNameSnapshot = request.DisplayNameSnapshot,
            UserPrincipalNameSnapshot = request.UserPrincipalNameSnapshot,
            MailSnapshot = request.MailSnapshot,
            UserTypeSnapshot = request.UserTypeSnapshot,
            AuthenticatedAtUtc = request.AuthenticatedAtUtc,
            RequestedAtUtc = request.RequestedAtUtc,
            Status = (int)request.Status,
            ReviewedAtUtc = request.ReviewedAtUtc,
            ReviewedByUserId = request.ReviewedByUserId,
            ProvisionedUserId = request.ProvisionedUserId
        };
}
