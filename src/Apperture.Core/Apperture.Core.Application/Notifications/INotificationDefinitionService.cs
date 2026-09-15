using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications;

public sealed record NotificationDefinitionCreateRequest(
    string DisplayName,
    string? Description,
    string IntegrationId,
    string RuleJson,
    string TemplateJson,
    bool IsEnabled = true,
    string? DefaultSeverity = null);

public sealed record NotificationDefinitionUpdateRequest(
    string? DisplayName,
    string? Description,
    string? RuleJson,
    string? TemplateJson,
    bool? IsEnabled,
    string? DefaultSeverity);

public sealed record NotificationDefinitionValidateRequest(
    string RuleJson,
    string TemplateJson,
    string? Origin,
    string? OwnerIntegrationId,
    IReadOnlyList<string>? EventTypes);

public sealed record NotificationDefinitionRenderPreviewRequest(
    string? TemplateJson,
    string? VariantKey,
    NotificationRenderSample Sample);

public interface INotificationDefinitionService
{
    ValueTask<IReadOnlyList<NotificationDefinition>> ListAsync(
        NotificationDefinitionListQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationDefinition?> GetAsync(
        string id,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationDefinition> CreateAsync(
        NotificationDefinitionCreateRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationDefinition?> UpdateAsync(
        string id,
        NotificationDefinitionUpdateRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationDefinition?> SetEnabledAsync(
        string id,
        bool isEnabled,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationValidationResult> ValidateAsync(
        NotificationDefinitionValidateRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<RenderedNotificationSnapshot> RenderPreviewAsync(
        string? definitionId,
        NotificationDefinitionRenderPreviewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pass-through to <see cref="INotificationDefinitionStore.GetIntegrationOwnerContextIdsAsync"/>: no
    /// additional authorization is applied here beyond what already gated the caller's definition list,
    /// since this only resolves ownership for Integration ids the caller can already see referenced.
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, string>> GetIntegrationOwnerContextIdsAsync(
        IReadOnlyCollection<string> integrationIds,
        CancellationToken cancellationToken = default);
}
