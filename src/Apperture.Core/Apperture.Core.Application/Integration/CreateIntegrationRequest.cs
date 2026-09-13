using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Core.Application.Integration;

/// <summary>
/// Request to create an integration. Epic 49, Epic 50.
/// </summary>
/// <param name="ScopeKey">Authorization scope. Unrelated to <paramref name="ResourceContextId"/>.</param>
/// <param name="ResourceContextId">
/// Owning ResourceContext. Optional: when absent the Integration is owned by the reserved Shared
/// context, so existing clients that omit it keep working. A supplied context must exist and be Active.
/// </param>
public sealed record CreateIntegrationRequest(
    string DisplayName,
    string ModuleId,
    string? ScopeKey = null,
    IReadOnlyDictionary<string, string>? Settings = null,
    ResourceContextId? ResourceContextId = null);
