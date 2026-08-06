using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// SDK-7 — bounded cross-integration shared-record reference lookup.
/// Authorization: authenticated actor with instance-scoped <c>integration.read</c>.
/// </summary>
public sealed class SharedRecordReferenceQueryService : ISharedRecordReferenceQueryService
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    private readonly AppertureDbContext _db;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;
    private readonly ILogger<SharedRecordReferenceQueryService> _logger;

    public SharedRecordReferenceQueryService(
        AppertureDbContext db,
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        ILogger<SharedRecordReferenceQueryService> logger)
    {
        _db = db;
        _principalContext = principalContext;
        _auth = auth;
        _logger = logger;
    }

    public async Task<SharedRecordReferencePage> GetReferencesAsync(
        SharedRecordIdentity identity,
        SharedRecordReferenceQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(cancellationToken).ConfigureAwait(false);
        var normalized = SharedRecordIdentityRules.ValidateAndNormalize(identity);
        query ??= new SharedRecordReferenceQuery();

        var skip = Math.Max(0, query.Skip);
        var take = query.Take <= 0 ? DefaultPageSize : Math.Min(query.Take, MaxPageSize);
        var typeLower = normalized.RecordType;
        var keyLower = normalized.BusinessKey.ToLowerInvariant();

        var q = from item in _db.OperationItems.AsNoTracking()
                join grp in _db.OperationGroups.AsNoTracking() on item.OperationGroupId equals grp.Id
                join integ in _db.Integrations.AsNoTracking() on grp.IntegrationId equals integ.Id into integJoin
                from integ in integJoin.DefaultIfEmpty()
                where item.SharedRecordType != null
                      && item.SharedBusinessKey != null
                      && item.SharedRecordType.ToLower() == typeLower
                      && item.SharedBusinessKey.ToLower() == keyLower
                select new
                {
                    item.Id,
                    item.OperationGroupId,
                    item.RunId,
                    item.SubjectKey,
                    item.DisplayLabel,
                    item.Status,
                    item.CompletedAtUtc,
                    item.CreatedAtUtc,
                    item.SharedRecordType,
                    item.SharedBusinessKey,
                    grp.ModuleId,
                    grp.IntegrationId,
                    IntegrationLabel = integ != null ? integ.DisplayName : null,
                };

        if (!string.IsNullOrWhiteSpace(query.IntegrationId))
            q = q.Where(x => x.IntegrationId == query.IntegrationId.Trim());

        if (!string.IsNullOrWhiteSpace(query.ModuleId))
            q = q.Where(x => x.ModuleId == query.ModuleId.Trim());

        if (!string.IsNullOrWhiteSpace(query.Status))
            q = q.Where(x => x.Status == query.Status.Trim());

        if (query.LookbackDays is > 0)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-query.LookbackDays.Value);
            q = q.Where(x => (x.CompletedAtUtc ?? x.CreatedAtUtc) >= cutoff);
        }

        var total = await q.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await q
            .OrderByDescending(x => x.CompletedAtUtc ?? x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows.Select(r =>
        {
            var sourceKind = DemoBusinessRecords.DemoBusinessRecordSourceKinds.FromModuleId(r.ModuleId);
            return new SharedRecordReference(
                RecordType: r.SharedRecordType!,
                BusinessKey: r.SharedBusinessKey!,
                OperationItemId: r.Id,
                OperationGroupId: r.OperationGroupId,
                RunId: r.RunId,
                IntegrationId: r.IntegrationId,
                IntegrationLabel: r.IntegrationLabel,
                ModuleId: r.ModuleId,
                SourceKind: string.IsNullOrWhiteSpace(sourceKind) ? null : sourceKind,
                SubjectKey: r.SubjectKey,
                DisplayLabel: r.DisplayLabel,
                Outcome: r.Status,
                OccurredAtUtc: r.CompletedAtUtc ?? r.CreatedAtUtc);
        }).ToArray();

        _logger.LogDebug(
            "Shared-record lookup {RecordType}/{BusinessKey}: total={Total} returned={Returned}",
            normalized.RecordType,
            normalized.BusinessKey,
            total,
            items.Length);

        return new SharedRecordReferencePage(
            normalized,
            items,
            total,
            skip,
            take,
            HasMore: skip + items.Length < total);
    }

    private async Task EnsureCanReadAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var permission = new PermissionDescriptor(
            ActionConstants.IntegrationRead,
            new ResourceDescriptor(ResourceTypeConstants.Integration, ScopeKeyConstants.Instance));

        var allowed = await _auth.EvaluateAsync(actor, permission, permission.Resource, null, cancellationToken)
            .ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to look up shared-record references.");
    }
}
