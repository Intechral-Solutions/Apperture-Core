using Apperture.ControlPlane.Security.Audit;
using Apperture.ControlPlane.Security.Models;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
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
        var items = new List<InventoryItemDto>();

        if (mode == InventoryModes.Resources)
            await CollectResourcesAsync(items, caps, cancellationToken).ConfigureAwait(false);
        else
            await CollectActivityAsync(items, caps, cancellationToken).ConfigureAwait(false);

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
        CancellationToken cancellationToken)
    {
        if (caps.CanManageModules)
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

        if (caps.CanViewIntegrations)
        {
            await TryCollectAsync(
                items,
                async () =>
                {
                    var rows = await _integrations.ListIntegrationsAsync(cancellationToken).ConfigureAwait(false);
                    foreach (var row in rows)
                        items.Add(MapIntegration(row));
                }).ConfigureAwait(false);
        }

        if (caps.CanViewConnections)
        {
            await TryCollectAsync(
                items,
                async () =>
                {
                    var rows = await _connections.ListConnectionsAsync(cancellationToken).ConfigureAwait(false);
                    foreach (var row in rows)
                        items.Add(MapConnection(row));
                }).ConfigureAwait(false);
        }

        if (caps.CanManageSchedules)
        {
            await TryCollectAsync(
                items,
                async () =>
                {
                    var rows = await _schedules.ListDefinitionsAsync(cancellationToken).ConfigureAwait(false);
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
                    var rows = await _notificationDefinitions
                        .ListAsync(new NotificationDefinitionListQuery(Take: SourceTake), cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var row in rows)
                        items.Add(MapNotificationDefinition(row));
                }).ConfigureAwait(false);
        }
    }

    private async Task CollectActivityAsync(
        List<InventoryItemDto> items,
        OperatorCapabilitiesDto caps,
        CancellationToken cancellationToken)
    {
        await TryCollectAsync(
            items,
            async () =>
            {
                var rows = await _runs
                    .ListRunsAsync(new RunQueryFilter(Take: SourceTake), cancellationToken)
                    .ConfigureAwait(false);
                foreach (var row in rows)
                    items.Add(MapRun(row));
            }).ConfigureAwait(false);

        if (caps.CanViewAudit)
        {
            await TryCollectAsync(
                items,
                async () =>
                {
                    var page = await _audit
                        .QueryAsync(
                            new AuditFilterCriteria(
                                FromUtc: _clock.GetUtcNow().Subtract(AuditWindow),
                                PageSize: SourceTake),
                            cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var row in page.Items)
                        items.Add(MapAudit(row));
                }).ConfigureAwait(false);
        }
    }

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
            InventoryItemModes.Resource);
    }

    private static InventoryItemDto MapIntegration(Integration row)
    {
        var id = row.Id.Value;
        var status = row.State.ToString();
        return new InventoryItemDto(
            id,
            InventoryTypeTokens.Integration,
            "Integration",
            row.DisplayName,
            status,
            status,
            $"/integrations/{id}",
            row.UpdatedAt,
            InventoryItemModes.Resource);
    }

    private static InventoryItemDto MapConnection(Connection row)
    {
        var id = row.Id.Value;
        var status = row.State.ToString();
        return new InventoryItemDto(
            id,
            InventoryTypeTokens.Connection,
            "Connection",
            row.Name,
            status,
            status,
            $"/connections/{id}",
            row.LastVerificationAtUtc,
            InventoryItemModes.Resource);
    }

    private static InventoryItemDto MapSchedule(ScheduleDefinition row)
    {
        var id = row.Id.Value;
        var status = row.Enabled ? "Enabled" : "Disabled";
        var updated = row.Lifecycle?.MaterialScheduleSemanticsChangedAtUtc
            ?? row.Lifecycle?.LastScheduleEnabledAtUtc
            ?? row.Lifecycle?.LastScheduleDisabledAtUtc;
        return new InventoryItemDto(
            id,
            InventoryTypeTokens.Schedule,
            "Schedule",
            row.Name,
            status,
            status,
            $"/schedules/{id}",
            updated,
            InventoryItemModes.Resource);
    }

    private static InventoryItemDto MapNotificationDefinition(NotificationDefinition row)
    {
        var id = row.Id.Value;
        var status = row.IsEnabled ? "Enabled" : "Disabled";
        return new InventoryItemDto(
            id,
            InventoryTypeTokens.NotificationDefinition,
            "Notification definition",
            row.DisplayName,
            status,
            status,
            $"/config/notifications/definitions/{id}",
            row.UpdatedAtUtc,
            InventoryItemModes.Resource);
    }

    private static InventoryItemDto MapRun(RunSummaryDto row)
    {
        var name = string.IsNullOrWhiteSpace(row.ExecutionDefinitionId)
            ? row.RunId
            : row.ExecutionDefinitionId;
        return new InventoryItemDto(
            row.RunId,
            InventoryTypeTokens.Run,
            "Run",
            name,
            row.Status,
            row.Status,
            $"/runs/{row.RunId}",
            row.CompletedAt ?? row.CreatedAt,
            InventoryItemModes.Activity);
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
        return new InventoryItemDto(
            row.Id,
            InventoryTypeTokens.AuditEvent,
            "Audit event",
            name,
            status,
            status,
            href,
            row.Timestamp,
            InventoryItemModes.Activity);
    }
}
