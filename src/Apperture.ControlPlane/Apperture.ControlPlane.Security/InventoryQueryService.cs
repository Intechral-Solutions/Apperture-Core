using Apperture.ControlPlane.Security.Audit;
using Apperture.ControlPlane.Security.Models;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.ModuleSdk.Abstractions;

namespace Apperture.ControlPlane.Security;

public sealed class InventoryQueryService : IInventoryQueryService
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
    public const int SourceTake = 100;
    public static readonly TimeSpan AuditWindow = TimeSpan.FromHours(24);

    private readonly IOperatorCapabilitiesQueryService _capabilities;
    private readonly IModuleRegistryService _modules;
    private readonly IIntegrationManagementService _integrations;
    private readonly IConnectionManagementService _connections;
    private readonly IScheduleManagementService _schedules;
    private readonly INotificationDefinitionService _notificationDefinitions;
    private readonly IRunQueryService _runs;
    private readonly IAuditTimelineQueryService _audit;
    private readonly TimeProvider _clock;

    public InventoryQueryService(
        IOperatorCapabilitiesQueryService capabilities,
        IModuleRegistryService modules,
        IIntegrationManagementService integrations,
        IConnectionManagementService connections,
        IScheduleManagementService schedules,
        INotificationDefinitionService notificationDefinitions,
        IRunQueryService runs,
        IAuditTimelineQueryService audit,
        TimeProvider clock)
    {
        _capabilities = capabilities;
        _modules = modules;
        _integrations = integrations;
        _connections = connections;
        _schedules = schedules;
        _notificationDefinitions = notificationDefinitions;
        _runs = runs;
        _audit = audit;
        _clock = clock;
    }

    public async ValueTask<InventoryQueryResult> QueryAsync(
        InventoryQuery request,
        CancellationToken cancellationToken = default)
    {
        var caps = await _capabilities.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        var mode = NormalizeMode(request.Mode);
        var context = request.Context ?? InventoryContextFilter.All;
        var items = new List<InventoryItemDto>();

        if (mode == InventoryModes.Resources)
            await CollectResourcesAsync(items, caps, context, cancellationToken).ConfigureAwait(false);
        else
            await CollectActivityAsync(items, caps, context, cancellationToken).ConfigureAwait(false);

        var availableTypes = items
            .Select(i => i.Type)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var filtered = items.Where(i => Matches(i, request)).ToList();
        var sorted = Sort(filtered, request.Sort);
        var take = NormalizeTake(request.Take);

        // When no explicit type filter is requested, a naive Take() can silently drop an entire
        // authorized type behind higher-volume or more-recently-updated types sharing the same
        // take budget (e.g. Modules disappearing behind Integrations/Connections). Guarantee at
        // least one representative per available type survives truncation. An explicit type
        // filter already returns every matching row regardless of take pressure from other
        // types, since filtering happens before this step.
        var page = string.IsNullOrWhiteSpace(request.Type)
            ? EnsureTypeRepresentation(sorted, take)
            : sorted.Take(take).ToList();

        return new InventoryQueryResult(page, availableTypes);
    }

    /// <summary>
    /// Ensures every distinct type present in <paramref name="sorted"/> has at least one item in
    /// the returned page, displacing the lowest-ranked item of whichever type currently holds the
    /// most page slots. Returned in the same relative order as <paramref name="sorted"/>.
    /// </summary>
    private static List<InventoryItemDto> EnsureTypeRepresentation(
        IReadOnlyList<InventoryItemDto> sorted,
        int take)
    {
        if (sorted.Count <= take)
            return sorted.Take(take).ToList();

        var pageIndexes = new List<int>(Enumerable.Range(0, take));
        var pageTypes = new HashSet<string>(
            pageIndexes.Select(i => sorted[i].Type),
            StringComparer.Ordinal);

        var missingTypes = sorted
            .Select(i => i.Type)
            .Distinct(StringComparer.Ordinal)
            .Where(t => !pageTypes.Contains(t))
            .ToList();

        foreach (var type in missingTypes)
        {
            var representativeIndex = -1;
            for (var i = 0; i < sorted.Count; i++)
            {
                if (!string.Equals(sorted[i].Type, type, StringComparison.Ordinal))
                    continue;
                representativeIndex = i;
                break;
            }
            if (representativeIndex < 0)
                continue;

            var typeToDisplace = pageIndexes
                .GroupBy(i => sorted[i].Type, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .First()
                .Key;
            var displaceAt = pageIndexes
                .Where(i => string.Equals(sorted[i].Type, typeToDisplace, StringComparison.Ordinal))
                .Max();

            pageIndexes.Remove(displaceAt);
            pageIndexes.Add(representativeIndex);
            pageTypes.Add(type);
        }

        pageIndexes.Sort();
        return pageIndexes.Select(i => sorted[i]).ToList();
    }

    private async Task CollectResourcesAsync(
        List<InventoryItemDto> items,
        OperatorCapabilitiesDto caps,
        InventoryContextFilter context,
        CancellationToken cancellationToken)
    {
        // Modules are always System-classified: under a real-context filter, no Module can ever match,
        // so the source is skipped entirely rather than fetched and filtered away in memory.
        if (caps.CanManageModules && context.Kind != InventoryContextFilterKind.Context)
        {
            await TryCollectAsync(
                items,
                async () =>
                {
                    var rows = await _modules.ListModulesAsync(cancellationToken).ConfigureAwait(false);
                    foreach (var row in rows)
                        items.Add(MapModule(row));
                }).ConfigureAwait(false);
        }

        // Integrations/Connections/Schedules are never System-classified: under the System filter, none
        // of them can ever match, so the source is skipped rather than fetched and filtered away.
        if (caps.CanViewIntegrations && context.Kind != InventoryContextFilterKind.System)
        {
            await TryCollectAsync(
                items,
                async () =>
                {
                    // The unfiltered overload is used for All rather than always calling the filtered
                    // overload with a null id: they are equivalent in production, but existing callers
                    // (tests included) that only stub the unfiltered overload must keep working unchanged.
                    var rows = context.Kind == InventoryContextFilterKind.Context
                        ? await _integrations.ListIntegrationsAsync(new ResourceContextId(context.ResourceContextId!), cancellationToken).ConfigureAwait(false)
                        : await _integrations.ListIntegrationsAsync(cancellationToken).ConfigureAwait(false);
                    foreach (var row in rows)
                        items.Add(MapIntegration(row));
                }).ConfigureAwait(false);
        }

        if (caps.CanViewConnections && context.Kind != InventoryContextFilterKind.System)
        {
            await TryCollectAsync(
                items,
                async () =>
                {
                    var rows = context.Kind == InventoryContextFilterKind.Context
                        ? await _connections.ListConnectionsAsync(new ResourceContextId(context.ResourceContextId!), cancellationToken).ConfigureAwait(false)
                        : await _connections.ListConnectionsAsync(cancellationToken).ConfigureAwait(false);
                    foreach (var row in rows)
                        items.Add(MapConnection(row));
                }).ConfigureAwait(false);
        }

        if (caps.CanManageSchedules && context.Kind != InventoryContextFilterKind.System)
        {
            await TryCollectAsync(
                items,
                async () =>
                {
                    var rows = context.Kind == InventoryContextFilterKind.Context
                        ? await _schedules.ListDefinitionsAsync(new ResourceContextId(context.ResourceContextId!), cancellationToken).ConfigureAwait(false)
                        : await _schedules.ListDefinitionsAsync(cancellationToken).ConfigureAwait(false);
                    foreach (var row in rows)
                        items.Add(MapSchedule(row));
                }).ConfigureAwait(false);
        }

        if (caps.CanViewNotificationDefinitions)
        {
            await TryCollectAsync(
                items,
                async () =>
                {
                    var query = context.Kind switch
                    {
                        InventoryContextFilterKind.System =>
                            new NotificationDefinitionListQuery(Take: SourceTake, SystemScopedOnly: true),
                        InventoryContextFilterKind.Context =>
                            new NotificationDefinitionListQuery(Take: SourceTake, OwnerResourceContextId: context.ResourceContextId),
                        _ => new NotificationDefinitionListQuery(Take: SourceTake)
                    };
                    var rows = await _notificationDefinitions.ListAsync(query, cancellationToken).ConfigureAwait(false);

                    var integrationIds = rows
                        .Where(r => string.Equals(r.Scope, NotificationDefinitionScopes.Integration, StringComparison.Ordinal)
                            && !string.IsNullOrEmpty(r.IntegrationId))
                        .Select(r => r.IntegrationId!)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    var owners = integrationIds.Count == 0
                        ? EmptyOwners
                        : await _notificationDefinitions
                            .GetIntegrationOwnerContextIdsAsync(integrationIds, cancellationToken)
                            .ConfigureAwait(false) ?? EmptyOwners;

                    foreach (var row in rows)
                        items.Add(MapNotificationDefinition(row, owners));
                }).ConfigureAwait(false);
        }
    }

    private async Task CollectActivityAsync(
        List<InventoryItemDto> items,
        OperatorCapabilitiesDto caps,
        InventoryContextFilter context,
        CancellationToken cancellationToken)
    {
        // Runs are never System-classified: under the System filter, no Run can ever match, so the
        // source is skipped rather than fetched and filtered away in memory (which would also defeat
        // the discoverability guarantee for the other filter values on this capped source).
        if (context.Kind != InventoryContextFilterKind.System)
        {
            await TryCollectAsync(
                items,
                async () =>
                {
                    var filter = context.Kind switch
                    {
                        InventoryContextFilterKind.NotRecorded =>
                            new RunQueryFilter(Take: SourceTake, ResourceContextIdIsNull: true),
                        InventoryContextFilterKind.Context =>
                            new RunQueryFilter(Take: SourceTake, ResourceContextId: context.ResourceContextId),
                        _ => new RunQueryFilter(Take: SourceTake)
                    };
                    var rows = await _runs.ListRunsAsync(filter, cancellationToken).ConfigureAwait(false);
                    foreach (var row in rows)
                        items.Add(MapRun(row));
                }).ConfigureAwait(false);
        }

        if (caps.CanViewAudit)
        {
            await TryCollectAsync(
                items,
                async () =>
                {
                    var fromUtc = _clock.GetUtcNow().Subtract(AuditWindow);
                    var criteria = context.Kind switch
                    {
                        InventoryContextFilterKind.System => new AuditFilterCriteria(
                            FromUtc: fromUtc, PageSize: SourceTake, ContextClassification: AuditContextClassificationFilter.System),
                        InventoryContextFilterKind.NotRecorded => new AuditFilterCriteria(
                            FromUtc: fromUtc, PageSize: SourceTake, ContextClassification: AuditContextClassificationFilter.NotRecorded),
                        InventoryContextFilterKind.Context => new AuditFilterCriteria(
                            FromUtc: fromUtc, PageSize: SourceTake, ResourceContextId: context.ResourceContextId),
                        _ => new AuditFilterCriteria(FromUtc: fromUtc, PageSize: SourceTake)
                    };
                    var page = await _audit.QueryAsync(criteria, cancellationToken).ConfigureAwait(false);
                    foreach (var row in page.Items)
                        items.Add(MapAudit(row));
                }).ConfigureAwait(false);
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyOwners =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static async Task TryCollectAsync(List<InventoryItemDto> items, Func<Task> collect)
    {
        var start = items.Count;
        try
        {
            await collect().ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            if (items.Count > start)
                items.RemoveRange(start, items.Count - start);
        }
    }

    private static bool Matches(InventoryItemDto item, InventoryQuery request)
    {
        // Cross-check only: the real filtering already happened at each source's query (see
        // CollectResourcesAsync/CollectActivityAsync) so a capped source's discoverability guarantee
        // holds. This re-derives the same predicate from the mapped item as a defense-in-depth check.
        if (request.Context is { Kind: not InventoryContextFilterKind.All } filter)
        {
            var contextMatches = filter.Kind switch
            {
                InventoryContextFilterKind.System => item.ResourceContextClassification == InventoryContextClassifications.System,
                InventoryContextFilterKind.NotRecorded => item.ResourceContextClassification == InventoryContextClassifications.NotRecorded,
                _ => string.Equals(item.ResourceContextId, filter.ResourceContextId, StringComparison.Ordinal)
            };
            if (!contextMatches)
                return false;
        }

        if (!string.IsNullOrWhiteSpace(request.Type)
            && !string.Equals(item.Type, request.Type.Trim(), StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (string.IsNullOrWhiteSpace(item.Status))
                return false;
            var needle = request.Status.Trim();
            if (!item.Status.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item.Status, needle, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (string.IsNullOrWhiteSpace(request.Q))
            return true;

        var q = request.Q.Trim();
        return Contains(item.Name, q)
            || Contains(item.Id, q)
            || Contains(item.TypeLabel, q);
    }

    private static bool Contains(string? value, string q) =>
        !string.IsNullOrEmpty(value) && value.Contains(q, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<InventoryItemDto> Sort(IReadOnlyList<InventoryItemDto> items, string? sort)
    {
        var key = string.IsNullOrWhiteSpace(sort) ? InventorySorts.Updated : sort.Trim();
        if (string.Equals(key, InventorySorts.Name, StringComparison.OrdinalIgnoreCase))
        {
            return items
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (string.Equals(key, InventorySorts.Status, StringComparison.OrdinalIgnoreCase))
        {
            return items
                .OrderBy(i => i.Status ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return items
            .OrderByDescending(i => i.UpdatedAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeMode(string? mode) =>
        string.Equals(mode, InventoryModes.Activity, StringComparison.OrdinalIgnoreCase)
            ? InventoryModes.Activity
            : InventoryModes.Resources;

    private static int NormalizeTake(int take)
    {
        if (take <= 0)
            return DefaultTake;
        return Math.Min(take, MaxTake);
    }

    private static InventoryItemDto MapModule(ModuleCatalogEntry row)
    {
        var id = row.Descriptor.ModuleId;
        var (status, label) = row.State switch
        {
            ModuleLifecycleState.InstalledEnabled => ("InstalledEnabled", "Installed (enabled)"),
            ModuleLifecycleState.InstalledDisabled => ("InstalledDisabled", "Installed (disabled)"),
            _ => ("Discovered", "Discovered")
        };
        return new InventoryItemDto(
            id,
            InventoryTypeTokens.Module,
            "Module",
            string.IsNullOrWhiteSpace(row.Descriptor.DisplayName) ? id : row.Descriptor.DisplayName,
            status,
            label,
            $"/modules/{id}",
            UpdatedAtUtc: row.UpdatedAtUtc,
            InventoryItemModes.Resource,
            ResourceContextId: null,
            ResourceContextClassification: InventoryContextClassifications.System);
    }

    private static InventoryItemDto MapIntegration(Integration row)
    {
        var id = row.Id.Value;
        var status = row.State.ToString();
        var (contextId, classification) = ClassifyContextId(row.ResourceContextId.Value);
        return new InventoryItemDto(
            id,
            InventoryTypeTokens.Integration,
            "Integration",
            row.DisplayName,
            status,
            status,
            $"/integrations/{id}",
            row.UpdatedAt,
            InventoryItemModes.Resource,
            contextId,
            classification);
    }

    private static InventoryItemDto MapConnection(Connection row)
    {
        var id = row.Id.Value;
        var status = row.State.ToString();
        var (contextId, classification) = ClassifyContextId(row.ResourceContextId.Value);
        return new InventoryItemDto(
            id,
            InventoryTypeTokens.Connection,
            "Connection",
            row.Name,
            status,
            status,
            $"/connections/{id}",
            row.LastVerificationAtUtc,
            InventoryItemModes.Resource,
            contextId,
            classification);
    }

    private static InventoryItemDto MapSchedule(ScheduleDefinition row)
    {
        var id = row.Id.Value;
        var status = row.Enabled ? "Enabled" : "Disabled";
        var updated = row.Lifecycle?.MaterialScheduleSemanticsChangedAtUtc
            ?? row.Lifecycle?.LastScheduleEnabledAtUtc
            ?? row.Lifecycle?.LastScheduleDisabledAtUtc;
        // ScheduleDefinition.ResourceContextId is nullable in the domain type even though creation
        // always resolves an owner (design gate §5.5 assumes it is always populated); default to Shared
        // defensively rather than throw for any pre-CTX-1 row that somehow slipped through unbackfilled.
        var (contextId, classification) = ClassifyContextId(row.ResourceContextId?.Value ?? ResourceContextIds.SharedValue);
        return new InventoryItemDto(
            id,
            InventoryTypeTokens.Schedule,
            "Schedule",
            row.Name,
            status,
            status,
            $"/schedules/{id}",
            updated,
            InventoryItemModes.Resource,
            contextId,
            classification);
    }

    private static InventoryItemDto MapNotificationDefinition(
        NotificationDefinition row,
        IReadOnlyDictionary<string, string> integrationOwners)
    {
        var id = row.Id.Value;
        var status = row.IsEnabled ? "Enabled" : "Disabled";
        var (contextId, classification) = ClassifyNotificationDefinition(row, integrationOwners);
        return new InventoryItemDto(
            id,
            InventoryTypeTokens.NotificationDefinition,
            "Notification definition",
            row.DisplayName,
            status,
            status,
            $"/config/notifications/definitions/{id}",
            row.UpdatedAtUtc,
            InventoryItemModes.Resource,
            contextId,
            classification);
    }

    private static InventoryItemDto MapRun(RunSummaryDto row)
    {
        var name = string.IsNullOrWhiteSpace(row.ExecutionDefinitionId)
            ? row.RunId
            : row.ExecutionDefinitionId;
        var (contextId, classification) = ClassifyRun(row.ResourceContextId);
        return new InventoryItemDto(
            row.RunId,
            InventoryTypeTokens.Run,
            "Run",
            name,
            row.Status,
            row.Status,
            $"/runs/{row.RunId}",
            row.CompletedAt ?? row.CreatedAt,
            InventoryItemModes.Activity,
            contextId,
            classification);
    }

    private static InventoryItemDto MapAudit(AuditTimelineEntry row)
    {
        var name = !string.IsNullOrWhiteSpace(row.Summary) ? row.Summary : row.Action;
        var status = string.IsNullOrWhiteSpace(row.Outcome) ? null : row.Outcome;
        var runId = row.RelatedReferences.FirstOrDefault(r =>
            string.Equals(r.Kind, "run", StringComparison.OrdinalIgnoreCase))?.Id;
        var href = string.IsNullOrWhiteSpace(runId)
            ? "/control/audit"
            : $"/control/audit?runId={Uri.EscapeDataString(runId)}";
        var (contextId, classification) = ClassifyAudit(row.ResourceContextId, row.Action);
        return new InventoryItemDto(
            row.Id,
            InventoryTypeTokens.AuditEvent,
            "Audit event",
            name,
            status,
            status,
            href,
            row.Timestamp,
            InventoryItemModes.Activity,
            contextId,
            classification);
    }

    /// <summary>A real ResourceContext id classifies Shared (the reserved id) or Application (anything else).</summary>
    private static (string? Id, string Classification) ClassifyContextId(string resourceContextId) =>
        ResourceContextIds.IsShared(resourceContextId)
            ? (resourceContextId, InventoryContextClassifications.Shared)
            : (resourceContextId, InventoryContextClassifications.Application);

    /// <summary>Runs are never System: a missing context is Not recorded, never inferred from anything else.</summary>
    private static (string? Id, string Classification) ClassifyRun(string? resourceContextId) =>
        resourceContextId is null ? (null, InventoryContextClassifications.NotRecorded) : ClassifyContextId(resourceContextId);

    /// <summary>
    /// Id-first: a non-null ResourceContextId always wins (this covers <c>resource_context.*</c> actions
    /// regardless of prefix). Only a null id falls back to <see cref="AuditSystemActionPrefixes"/>.
    /// <see cref="AuditCategoryResolver"/> is deliberately not used here (design gate §5.5: its "other"
    /// category conflates a context-eligible family with System-shaped families it does not recognize).
    /// </summary>
    private static (string? Id, string Classification) ClassifyAudit(string? resourceContextId, string action) =>
        resourceContextId is not null
            ? ClassifyContextId(resourceContextId)
            : (null, AuditSystemActionPrefixes.Matches(action) ? InventoryContextClassifications.System : InventoryContextClassifications.NotRecorded);

    /// <summary>
    /// Platform/Module scope is always System. Integration scope resolves through the batched owner
    /// lookup; an id absent from it (a hard-deleted Integration -- a known, out-of-scope-to-fix orphan
    /// condition, design gate §13) classifies Unknown rather than System or Not recorded.
    /// </summary>
    private static (string? Id, string Classification) ClassifyNotificationDefinition(
        NotificationDefinition row,
        IReadOnlyDictionary<string, string> integrationOwners)
    {
        if (!string.Equals(row.Scope, NotificationDefinitionScopes.Integration, StringComparison.Ordinal))
            return (null, InventoryContextClassifications.System);

        if (string.IsNullOrEmpty(row.IntegrationId) || !integrationOwners.TryGetValue(row.IntegrationId, out var owner))
            return (null, InventoryContextClassifications.Unknown);

        return ClassifyContextId(owner);
    }
}
