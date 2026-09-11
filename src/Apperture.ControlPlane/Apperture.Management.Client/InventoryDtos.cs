namespace Apperture.Management.Client;

public sealed record InventoryQueryRequest(
    string Mode = "resources",
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
