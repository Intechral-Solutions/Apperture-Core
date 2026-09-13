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
    int Take = 100);

public sealed record InventoryItemDto(
    string Id,
    string Type,
    string TypeLabel,
    string Name,
    string? Status,
    string? StatusLabel,
    string Href,
    DateTimeOffset? UpdatedAtUtc,
    string Mode);

public sealed record InventoryQueryResult(
    IReadOnlyList<InventoryItemDto> Items,
    IReadOnlyList<string> AvailableTypes);
