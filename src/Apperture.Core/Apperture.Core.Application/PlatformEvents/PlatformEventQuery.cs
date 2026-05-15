namespace Apperture.Core.Application.PlatformEvents;

public sealed record PlatformEventQuery(
    string? IntegrationId = null,
    string? EventType = null,
    string? Severity = null,
    DateTimeOffset? FromOccurredAtUtc = null,
    DateTimeOffset? ToOccurredAtUtc = null,
    int Take = 50);

public sealed record PlatformEventQueryPage(
    IReadOnlyList<PlatformEventEnvelope> Items,
    bool HasMore);
