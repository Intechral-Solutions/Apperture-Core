namespace Apperture.ControlPlane.Security;

/// <summary>
/// Aggregated administrative explorer over existing list/query services. Not a persisted catalog.
/// </summary>
public interface IInventoryQueryService
{
    ValueTask<InventoryQueryResult> QueryAsync(
        InventoryQuery request,
        CancellationToken cancellationToken = default);
}

public static class InventoryModes
{
    public const string Resources = "resources";
    public const string Activity = "activity";
}

public static class InventoryItemModes
{
    public const string Resource = "resource";
    public const string Activity = "activity";
}

public static class InventoryTypeTokens
{
    public const string Module = "module";
    public const string Integration = "integration";
    public const string Connection = "connection";
    public const string Schedule = "schedule";
    public const string NotificationDefinition = "notificationDefinition";
    public const string Run = "run";
    public const string AuditEvent = "auditEvent";
}

public static class InventorySorts
{
    public const string Name = "name";
    public const string Status = "status";
    public const string Updated = "updated";
}

public sealed record InventoryQuery(
    string Mode = InventoryModes.Resources,
    string? Type = null,
    string? Q = null,
    string? Status = null,
    string? Sort = null,
    int Take = 100,
    InventoryContextFilter? Context = null,
    IReadOnlyList<string>? Types = null);

public sealed record InventoryItemDto(
    string Id,
    string Type,
    string TypeLabel,
    string Name,
    string? Status,
    string? StatusLabel,
    string Href,
    DateTimeOffset? UpdatedAtUtc,
    string Mode,
    string? ResourceContextId = null,
    string ResourceContextClassification = InventoryContextClassifications.NotRecorded);

public sealed record InventoryQueryResult(
    IReadOnlyList<InventoryItemDto> Items,
    IReadOnlyList<string> AvailableTypes,
    int TotalMatching = 0);

/// <summary>Context classification tokens surfaced on <see cref="InventoryItemDto.ResourceContextClassification"/> (design gate §5.5).</summary>
public static class InventoryContextClassifications
{
    public const string System = "system";
    public const string Shared = "shared";
    public const string Application = "application";
    public const string NotRecorded = "notRecorded";

    /// <summary>
    /// An Integration-scoped NotificationDefinition whose owning Integration was hard-deleted (a known,
    /// out-of-scope-to-fix orphan condition -- see design gate §13). Visible only under the "all" filter;
    /// CTX-3A adds no Unknown filter option.
    /// </summary>
    public const string Unknown = "unknown";
}

/// <summary>Inventory's own local Context filter (independent of the shell's View selector; design gate §5.5).</summary>
public enum InventoryContextFilterKind { All, System, NotRecorded, Context }

public sealed record InventoryContextFilter(InventoryContextFilterKind Kind, string? ResourceContextId = null)
{
    public static InventoryContextFilter All { get; } = new(InventoryContextFilterKind.All);
    public static InventoryContextFilter System { get; } = new(InventoryContextFilterKind.System);
    public static InventoryContextFilter NotRecorded { get; } = new(InventoryContextFilterKind.NotRecorded);
    public static InventoryContextFilter ForContext(string resourceContextId) => new(InventoryContextFilterKind.Context, resourceContextId);
}
