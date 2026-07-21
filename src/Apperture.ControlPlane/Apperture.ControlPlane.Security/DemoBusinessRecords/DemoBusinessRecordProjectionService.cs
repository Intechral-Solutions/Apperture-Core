using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security.DemoBusinessRecords;

public interface IDemoBusinessRecordProjectionService
{
    Task<DemoBusinessRecordListResult> ListCustomersAsync(
        DemoBusinessRecordListQuery query,
        CancellationToken cancellationToken = default);

    Task<DemoBusinessRecordProjection?> GetCustomerAsync(
        string businessKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-time demo projection over Sample.FileImport / Sample.ApiPolling operation items.
/// Authorization: authenticated actor must hold <c>integration.read</c> at instance scope.
/// </summary>
public sealed class DemoBusinessRecordProjectionService : IDemoBusinessRecordProjectionService
{
    public static readonly string[] DemoModuleIds =
    [
        "Sample.FileImport",
        "Sample.ApiPolling",
    ];

    private readonly AppertureDbContext _db;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;

    public DemoBusinessRecordProjectionService(
        AppertureDbContext db,
        IPrincipalContext principalContext,
        IAuthorizationService auth)
    {
        _db = db;
        _principalContext = principalContext;
        _auth = auth;
    }

    public async Task<DemoBusinessRecordListResult> ListCustomersAsync(
        DemoBusinessRecordListQuery query,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanReadDemoProjectionAsync(cancellationToken).ConfigureAwait(false);
        var all = await LoadProjectionsAsync(cancellationToken).ConfigureAwait(false);
        return DemoBusinessRecordProjector.ApplyListQuery(all, query);
    }

    public async Task<DemoBusinessRecordProjection?> GetCustomerAsync(
        string businessKey,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanReadDemoProjectionAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(businessKey))
            return null;

        var all = await LoadProjectionsAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(r =>
            string.Equals(r.BusinessKey, businessKey.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    async Task EnsureCanReadDemoProjectionAsync(CancellationToken cancellationToken)
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
            throw new UnauthorizedAccessException("Not authorized to read demo business records.");
    }

    async Task<IReadOnlyList<DemoBusinessRecordProjection>> LoadProjectionsAsync(CancellationToken cancellationToken)
    {
        var moduleIds = DemoModuleIds;
        var rows = await (
                from item in _db.OperationItems.AsNoTracking()
                join grp in _db.OperationGroups.AsNoTracking() on item.OperationGroupId equals grp.Id
                where moduleIds.Contains(grp.ModuleId)
                select new
                {
                    item.Id,
                    item.OperationGroupId,
                    item.RunId,
                    item.SubjectKey,
                    item.DisplayLabel,
                    item.MetadataJson,
                    item.ChangeSummaryJson,
                    item.CompletedAtUtc,
                    item.CreatedAtUtc,
                    grp.ModuleId,
                    grp.IntegrationId,
                    grp.OperationKind,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var observations = new List<DemoBusinessRecordObservationInput>(rows.Count);
        foreach (var row in rows)
        {
            var obs = DemoBusinessRecordProjector.TryParseItemObservation(
                row.Id,
                row.OperationGroupId,
                row.RunId,
                row.ModuleId,
                row.IntegrationId,
                row.OperationKind,
                row.SubjectKey,
                row.DisplayLabel,
                row.MetadataJson,
                row.ChangeSummaryJson,
                row.CompletedAtUtc,
                row.CreatedAtUtc);
            if (obs is not null)
                observations.Add(obs);
        }

        return DemoBusinessRecordProjector.Project(observations);
    }
}
