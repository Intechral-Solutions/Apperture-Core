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
    bool SystemScopedOnly = false,
    IReadOnlyList<string>? Origins = null);

public sealed record NotificationDefinitionCreateCommand(
    string DisplayName,
    string? Description,
    string IntegrationId,
    string RuleJson,
    string TemplateJson,
    bool IsEnabled,
    string? DefaultSeverity,
    string? CreatedByActorId,
    bool ToastEnabledByDefault = false);

public sealed record NotificationDefinitionUpdateCommand(
    string? DisplayName,
    string? Description,
    string? RuleJson,
    string? TemplateJson,
    bool? IsEnabled,
    string? DefaultSeverity,
    string? UpdatedByActorId,
    bool? ToastEnabledByDefault = null);

public interface INotificationDefinitionStore
{
    ValueTask<IReadOnlyList<NotificationDefinition>> ListAsync(
        NotificationDefinitionListQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<NotificationDefinition?> GetAsync(
        NotificationDefinitionId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an integration-scoped user definition in one transaction that first takes the integration row lock and requires the
    /// integration to exist, and holds the lock to the commit (AUD-1.4): an integration delete that wins the lock makes this refuse,
    /// and a definition that commits first is visible to the delete's blocker count.
    /// </summary>
    /// <param name="reauthorize">
    /// The caller's authorization check, run again once the lock is held, so a decision taken before an ownership move committed
    /// is not acted on afterwards. The definition is integration-scoped, so its authorization depends on the integration: a
    /// caller must pass the check it made before the call and must not leave it out to skip the revalidation. Null is
    /// legitimate only for a caller that made no integration-dependent decision. The definition service passes it.
    /// </param>
    /// <exception cref="InvalidOperationException">The integration does not exist.</exception>
    ValueTask<NotificationDefinition> CreateUserDefinitionAsync(
        NotificationDefinitionCreateCommand command,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, ValueTask>? reauthorize = null);

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
