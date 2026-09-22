using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications;

public sealed record NotificationDefinitionListQuery(
    string? IntegrationId = null,
    string? ApplicableModuleId = null,
    string? EventType = null,
    bool? IsEnabled = null,
    string? Origin = null,
    int Take = 100,
    string? OwnerResourceContextId = null,
    bool SystemScopedOnly = false);

public sealed record NotificationDefinitionCreateCommand(
    string DisplayName,
    string? Description,
    string IntegrationId,
    string RuleJson,
    string TemplateJson,
    bool IsEnabled,
    string? DefaultSeverity,
    string? CreatedByActorId);

public sealed record NotificationDefinitionUpdateCommand(
    string? DisplayName,
    string? Description,
    string? RuleJson,
    string? TemplateJson,
    bool? IsEnabled,
    string? DefaultSeverity,
    string? UpdatedByActorId);

public interface INotificationDefinitionStore
{
    ValueTask<IReadOnlyList<NotificationDefinition>> ListAsync(
        NotificationDefinitionListQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationDefinition?> GetAsync(
        NotificationDefinitionId id,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationDefinition> CreateUserDefinitionAsync(
        NotificationDefinitionCreateCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationDefinition?> UpdateAsync(
        NotificationDefinitionId id,
        NotificationDefinitionUpdateCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationDefinition?> SetEnabledAsync(
        NotificationDefinitionId id,
        bool isEnabled,
        string? updatedByActorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user-created definition and its subordinate current-state data (subscriptions,
    /// subscription user overrides, and per-user notification channel preferences). Returns false when no
    /// such definition exists.
    /// </summary>
    /// <remarks>
    /// Refuses built-in definitions, mirroring <see cref="UpdateAsync"/>'s existing immutability rule for
    /// built-in rule/template JSON. Historical rows -- <c>NotificationInstances</c> above all -- keep their
    /// now-dangling <c>NotificationDefinitionId</c>, exactly as CTX-2 established for every other kind of
    /// current-state removal.
    /// </remarks>
    ValueTask<bool> DeleteAsync(NotificationDefinitionId id, CancellationToken cancellationToken = default);

    ValueTask<bool> ExistsDisplayNameAsync(
        string scope,
        string? integrationId,
        string displayName,
        NotificationDefinitionId? excludingId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batched owner lookup for Integration-scoped NotificationDefinitions: given a set of Integration
    /// ids (of definitions the caller can already see), returns each Integration's current
    /// <c>ResourceContextId</c> directly from the Integrations table -- not through
    /// <c>IIntegrationManagementService</c>, so classification does not depend on the operator's
    /// Integration-view capability. An id whose Integration no longer exists (hard-deleted) is simply
    /// absent from the result; callers must treat a missing id as "unknown", not as an error.
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, string>> GetIntegrationOwnerContextIdsAsync(
        IReadOnlyCollection<string> integrationIds,
        CancellationToken cancellationToken = default);
}
