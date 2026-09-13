namespace Apperture.Core.Application.PlatformEvents;

public sealed record PlatformEventQuery(
    string? IntegrationId = null,
    string? EventType = null,
    string? Severity = null,
    DateTimeOffset? FromOccurredAtUtc = null,
    DateTimeOffset? ToOccurredAtUtc = null,
    string? RunId = null,
    string? ModuleId = null,
    int Take = 50,
    string? ResourceContextId = null);

public sealed record PlatformEventQueryPage(
    IReadOnlyList<PlatformEventEnvelope> Items,
    bool HasMore);
