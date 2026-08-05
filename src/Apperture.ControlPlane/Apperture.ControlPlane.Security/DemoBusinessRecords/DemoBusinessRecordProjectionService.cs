using Apperture.Core.Application.OperationItems;
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

    Task<DemoBusinessRecordActivityPage?> GetCustomerActivityAsync(
        string businessKey,
        DemoBusinessRecordActivityQuery query,
        CancellationToken cancellationToken = default);

    Task<DemoBusinessRecordActivitySummary?> GetCustomerActivitySummaryAsync(
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

    public async Task<DemoBusinessRecordActivityPage?> GetCustomerActivityAsync(
        string businessKey,
        DemoBusinessRecordActivityQuery query,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanReadDemoProjectionAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(businessKey))
            return null;

        var key = businessKey.Trim();
        var exists = await CustomerExistsAsync(key, cancellationToken).ConfigureAwait(false);
        if (!exists)
            return null;

        var skip = DemoBusinessRecordActivityMapper.ClampSkip(query.Skip);
        var take = DemoBusinessRecordActivityMapper.ClampTake(query.Take);

        var filtered = await BuildActivityRowsAsync(key, query, cancellationToken).ConfigureAwait(false);
        var total = filtered.Count;
        var pageRows = filtered.Skip(skip).Take(take).ToList();
        var entries = pageRows.Select(MapEntry).ToList();

        return new DemoBusinessRecordActivityPage(
            entries,
            total,
            skip,
            take,
            HasMore: skip + entries.Count < total);
    }

    public async Task<DemoBusinessRecordActivitySummary?> GetCustomerActivitySummaryAsync(
        string businessKey,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanReadDemoProjectionAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(businessKey))
            return null;

        var key = businessKey.Trim();
        var exists = await CustomerExistsAsync(key, cancellationToken).ConfigureAwait(false);
        if (!exists)
            return null;

        var rows = await BuildActivityRowsAsync(
                key,
                new DemoBusinessRecordActivityQuery(),
                cancellationToken)
            .ConfigureAwait(false);

        return new DemoBusinessRecordActivitySummary(
            key,
            rows.Count,
            rows.Count(r => r.RequiresAttention),
            rows.Count == 0 ? null : rows[0].OccurredAtUtc);
    }

    async Task<bool> CustomerExistsAsync(string businessKey, CancellationToken cancellationToken)
    {
        var all = await LoadProjectionsAsync(cancellationToken).ConfigureAwait(false);
        return all.Any(r => string.Equals(r.BusinessKey, businessKey, StringComparison.OrdinalIgnoreCase));
    }

    async Task<List<ActivityRow>> BuildActivityRowsAsync(
        string businessKey,
        DemoBusinessRecordActivityQuery query,
        CancellationToken cancellationToken)
    {
        var moduleIds = DemoModuleIds;
        var customerLiteral = DemoBusinessRecordActivityMapper.MetadataContainsCustomerRecordLiteral;
        var moduleFilter = DemoBusinessRecordActivityMapper.ModuleIdForSourceKind(query.SourceKind);

        var q = from item in _db.OperationItems.AsNoTracking()
                join grp in _db.OperationGroups.AsNoTracking() on item.OperationGroupId equals grp.Id
                join run in _db.OperationRuns.AsNoTracking() on item.RunId equals run.Id into runJoin
                from run in runJoin.DefaultIfEmpty()
                join integ in _db.Integrations.AsNoTracking() on grp.IntegrationId equals integ.Id into integJoin
                from integ in integJoin.DefaultIfEmpty()
                where moduleIds.Contains(grp.ModuleId)
                      && item.SubjectKey != null
                      && item.SubjectKey == businessKey
                      && item.MetadataJson != null
                      && item.MetadataJson.Contains(customerLiteral)
                select new
                {
                    item.Id,
                    item.OperationGroupId,
                    item.RunId,
                    item.SubjectKey,
                    item.DisplayLabel,
                    ItemStatus = item.Status,
                    item.ErrorCode,
                    item.ErrorMessage,
                    item.IsRetryable,
                    item.RetryCount,
                    item.MetadataJson,
                    item.ChangeSummaryJson,
                    item.CompletedAtUtc,
                    item.CreatedAtUtc,
                    grp.ModuleId,
                    grp.IntegrationId,
                    grp.OperationKind,
                    grp.SourceLabel,
                    GroupStatus = grp.Status,
                    RunStatus = run != null ? run.Status : null,
                    IntegrationDisplayName = integ != null ? integ.DisplayName : null,
                };

        if (!string.IsNullOrWhiteSpace(moduleFilter))
            q = q.Where(x => x.ModuleId == moduleFilter);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim();
            q = q.Where(x => x.ItemStatus == status);
        }

        if (query.AttentionOnly)
            q = q.Where(x => x.ItemStatus == OperationItemStatuses.Failed || x.IsRetryable);

        if (query.LookbackDays is > 0)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-query.LookbackDays.Value);
            q = q.Where(x => (x.CompletedAtUtc ?? x.CreatedAtUtc) >= cutoff);
        }

        var raw = await q.ToListAsync(cancellationToken).ConfigureAwait(false);

        var rows = new List<ActivityRow>(raw.Count);
        foreach (var row in raw)
        {
            if (!DemoBusinessRecordActivityMapper.MatchesCustomerRecordMetadata(row.MetadataJson))
                continue;

            var observed = DemoBusinessRecordProjector.TryParseItemObservation(
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
            if (observed is null
                || !string.Equals(observed.BusinessKey, businessKey, StringComparison.OrdinalIgnoreCase))
                continue;

            var sourceKind = DemoBusinessRecordSourceKinds.FromModuleId(row.ModuleId);
            var sourceKindLabel = DemoBusinessRecordSourceKinds.ToDisplayLabel(sourceKind);
            var changeKind = DemoBusinessRecordActivityMapper.ReadChangeKind(
                row.MetadataJson,
                row.ChangeSummaryJson);
            var occurred = DemoBusinessRecordActivityMapper.ResolveOccurredAt(
                row.CompletedAtUtc,
                row.CreatedAtUtc,
                row.MetadataJson);

            rows.Add(new ActivityRow(
                BusinessKey: businessKey,
                OccurredAtUtc: occurred,
                RunId: row.RunId,
                RunStatus: row.RunStatus,
                IntegrationId: row.IntegrationId,
                IntegrationLabel: DemoBusinessRecordActivityMapper.ResolveIntegrationLabel(
                    row.IntegrationDisplayName,
                    row.SourceLabel,
                    sourceKindLabel),
                SourceKind: sourceKind,
                SourceKindLabel: sourceKindLabel,
                OperationGroupId: row.OperationGroupId,
                OperationItemId: row.Id,
                OperationKind: row.OperationKind,
                OperationLabel: DemoBusinessRecordActivityMapper.ResolveOperationLabel(
                    row.OperationKind,
                    changeKind),
                SubjectKey: row.SubjectKey,
                DisplayLabel: row.DisplayLabel,
                ItemStatus: row.ItemStatus,
                ChangeKind: changeKind,
                ErrorCode: row.ErrorCode,
                ErrorMessage: row.ErrorMessage,
                IsRetryable: row.IsRetryable,
                RetryCount: row.RetryCount,
                RequiresAttention: DemoBusinessRecordActivityMapper.IsAttentionItem(
                    row.ItemStatus,
                    row.IsRetryable),
                WasRetried: row.RetryCount > 0,
                SourceLabel: row.SourceLabel,
                GroupStatus: row.GroupStatus));
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            rows = rows.Where(r =>
                    ContainsIgnoreCase(r.SubjectKey, term)
                    || ContainsIgnoreCase(r.DisplayLabel, term)
                    || ContainsIgnoreCase(r.IntegrationLabel, term)
                    || ContainsIgnoreCase(r.OperationLabel, term)
                    || ContainsIgnoreCase(r.SourceLabel, term)
                    || ContainsIgnoreCase(r.SourceKindLabel, term)
                    || ContainsIgnoreCase(r.OperationKind, term))
                .ToList();
        }

        return rows
            .OrderByDescending(r => r.OccurredAtUtc)
            .ThenByDescending(r => r.OperationItemId, StringComparer.Ordinal)
            .ToList();
    }

    static DemoBusinessRecordActivityEntry MapEntry(ActivityRow row) =>
        new(
            row.BusinessKey,
            row.OccurredAtUtc,
            row.RunId,
            row.RunStatus,
            row.IntegrationId,
            row.IntegrationLabel,
            row.SourceKind,
            row.SourceKindLabel,
            row.OperationGroupId,
            row.OperationItemId,
            row.OperationKind,
            row.OperationLabel,
            row.SubjectKey,
            row.DisplayLabel,
            row.ItemStatus,
            row.ChangeKind,
            row.ErrorCode,
            row.ErrorMessage,
            row.IsRetryable,
            row.RetryCount,
            row.RequiresAttention,
            row.WasRetried,
            row.SourceLabel,
            row.GroupStatus);

    static bool ContainsIgnoreCase(string? haystack, string needle) =>
        !string.IsNullOrWhiteSpace(haystack)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private sealed record ActivityRow(
        string BusinessKey,
        DateTimeOffset OccurredAtUtc,
        string RunId,
        string? RunStatus,
        string? IntegrationId,
        string IntegrationLabel,
        string SourceKind,
        string SourceKindLabel,
        string OperationGroupId,
        string OperationItemId,
        string? OperationKind,
        string? OperationLabel,
        string? SubjectKey,
        string? DisplayLabel,
        string ItemStatus,
        string? ChangeKind,
        string? ErrorCode,
        string? ErrorMessage,
        bool IsRetryable,
        int RetryCount,
        bool RequiresAttention,
        bool WasRetried,
        string? SourceLabel,
        string? GroupStatus);
}
