namespace Apperture.Management.Client;

public sealed record InventoryQueryRequest(
    string Mode = "resources",
    string? Type = null,
    string? Q = null,
    string? Status = null,
    string? Sort = null,
    int Take = 100,
    string? Context = null);

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
    string ResourceContextClassification = "notRecorded");

public sealed record InventoryQueryResult(
    IReadOnlyList<InventoryItemDto> Items,
    IReadOnlyList<string> AvailableTypes);
