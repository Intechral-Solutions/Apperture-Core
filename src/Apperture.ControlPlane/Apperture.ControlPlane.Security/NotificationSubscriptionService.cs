using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Notifications;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

public sealed class NotificationSubscriptionService : INotificationSubscriptionService
{
    private readonly INotificationSubscriptionStore _subscriptions;
    private readonly INotificationDefinitionStore _definitions;
    private readonly INotificationRecipientResolver _resolver;
    private readonly ManagedContactRecipientProvisioner _managedContactProvisioner;
    private readonly NotificationDefinitionAuthorization _authorization;
    private readonly IPrincipalContext _principalContext;

    public NotificationSubscriptionService(
        INotificationSubscriptionStore subscriptions,
        INotificationDefinitionStore definitions,
        INotificationRecipientResolver resolver,
        ManagedContactRecipientProvisioner managedContactProvisioner,
        NotificationDefinitionAuthorization authorization,
        IPrincipalContext principalContext)
    {
        _subscriptions = subscriptions;
        _definitions = definitions;
        _resolver = resolver;
        _managedContactProvisioner = managedContactProvisioner;
        _authorization = authorization;
        _principalContext = principalContext;
    }

    public async ValueTask<IReadOnlyList<NotificationSubscription>> ListByDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        var definition = await RequireDefinitionAsync(definitionId, manage: false, cancellationToken)
            .ConfigureAwait(false);
        await _authorization
            .RequireDefinitionAccessAsync(definition.IntegrationId, manage: false, cancellationToken)
            .ConfigureAwait(false);
        return await _subscriptions.ListByDefinitionAsync(definitionId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<NotificationSubscription>> ListByIntegrationAsync(
        string integrationId,
        string? definitionId,
        CancellationToken cancellationToken = default)
    {
        await _authorization.RequireIntegrationReadAsync(integrationId, cancellationToken).ConfigureAwait(false);
        return await _subscriptions.ListByIntegrationAsync(integrationId, definitionId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<NotificationSubscription> AddRecipientAsync(
        string definitionId,
        NotificationSubscriptionAddRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = await RequireDefinitionAsync(definitionId, manage: true, cancellationToken)
            .ConfigureAwait(false);
        await _authorization
            .RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        ValidateIntegrationScope(definition, request.IntegrationId);

        var (kind, recipientRef) = await ResolveRecipientAsync(request, cancellationToken).ConfigureAwait(false);
        ValidateRecipientKind(kind);

        var actorId = await GetActorIdAsync(cancellationToken).ConfigureAwait(false);

        // An integration-scoped definition is authorized against its integration, so that check is run again once the store holds
        // the integration row lock. A platform- or module-scoped definition is authorized by the definition alone: the integration id
        // is only a row to refer to, and the store still locks it and requires it to exist.
        Func<CancellationToken, ValueTask>? reauthorize = string.IsNullOrWhiteSpace(definition.IntegrationId)
            ? null
            : ct => _authorization.RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, ct);

        return await _subscriptions.AddAsync(
                new NotificationSubscriptionCreateCommand(
                    definitionId,
                    request.IntegrationId,
                    kind,
                    recipientRef,
                    request.DeliveryMode,
                    NotificationSubscriptionOrigins.AdminAssigned,
                    IsEnabled: true,
                    actorId),
                cancellationToken,
                reauthorize)
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> RemoveAsync(
        string definitionId,
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var definition = await RequireDefinitionAsync(definitionId, manage: true, cancellationToken)
            .ConfigureAwait(false);
        await _authorization
            .RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        var sub = await _subscriptions.GetAsync(NotificationSubscriptionId.From(subscriptionId), cancellationToken)
            .ConfigureAwait(false);
        if (sub is null || !string.Equals(sub.NotificationDefinitionId, definitionId, StringComparison.Ordinal))
            return false;

        return await _subscriptions.RemoveAsync(NotificationSubscriptionId.From(subscriptionId), cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<NotificationSubscription?> PatchAsync(
        string definitionId,
        string subscriptionId,
        NotificationSubscriptionPatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = await RequireDefinitionAsync(definitionId, manage: true, cancellationToken)
            .ConfigureAwait(false);
        await _authorization
            .RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        var sub = await _subscriptions.GetAsync(NotificationSubscriptionId.From(subscriptionId), cancellationToken)
            .ConfigureAwait(false);
        if (sub is null || !string.Equals(sub.NotificationDefinitionId, definitionId, StringComparison.Ordinal))
            return null;

        var actorId = await GetActorIdAsync(cancellationToken).ConfigureAwait(false);
        if (request.IsEnabled is { } enabled)
        {
            return await _subscriptions
                .SetEnabledAsync(NotificationSubscriptionId.From(subscriptionId), enabled, actorId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(request.DeliveryMode))
        {
            ValidateDeliveryMode(request.DeliveryMode);
            return await _subscriptions
                .SetDeliveryModeAsync(
                    NotificationSubscriptionId.From(subscriptionId),
                    request.DeliveryMode,
                    actorId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return sub;
    }

    public async ValueTask<NotificationSubscriptionUserOverride> SetUserOptOutAsync(
        string definitionId,
        NotificationUserOptOutRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = await RequireDefinitionAsync(definitionId, manage: true, cancellationToken)
            .ConfigureAwait(false);
        await _authorization
            .RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.UserId))
            throw new InvalidOperationException("User id is required.");

        var actorId = await GetActorIdAsync(cancellationToken).ConfigureAwait(false);
        return await _subscriptions.SetUserOptOutAsync(
                new NotificationSubscriptionUserOverrideCommand(
                    request.UserId,
                    definitionId,
                    request.OperationalTeamId,
                    actorId),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> RemoveUserOptOutAsync(
        string definitionId,
        NotificationUserOptOutRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = await RequireDefinitionAsync(definitionId, manage: true, cancellationToken)
            .ConfigureAwait(false);
        await _authorization
            .RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.UserId))
            throw new InvalidOperationException("User id is required.");

        return await _subscriptions.RemoveUserOptOutAsync(
                request.UserId,
                definitionId,
                request.OperationalTeamId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<NotificationRecipientResolutionPreview> ResolveRecipientsPreviewAsync(
        string definitionId,
        NotificationRecipientResolveRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = await RequireDefinitionAsync(definitionId, manage: false, cancellationToken)
            .ConfigureAwait(false);
        await _authorization
            .RequireDefinitionAccessAsync(definition.IntegrationId, manage: false, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.IntegrationId)
            && !string.IsNullOrWhiteSpace(definition.IntegrationId)
            && !string.Equals(definition.IntegrationId, request.IntegrationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Integration id does not match the notification definition scope.");
        }

        return await _resolver
            .ResolvePreviewAsync(definitionId, request.IntegrationId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<NotificationDefinition> RequireDefinitionAsync(
        string definitionId,
        bool manage,
        CancellationToken cancellationToken)
    {
        var definition = await _definitions
            .GetAsync(NotificationDefinitionId.From(definitionId), cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            throw new InvalidOperationException($"Notification definition {definitionId} not found.");
        return definition;
    }

    private static void ValidateIntegrationScope(NotificationDefinition definition, string? integrationId)
    {
        if (!string.IsNullOrWhiteSpace(definition.IntegrationId))
        {
            if (string.IsNullOrWhiteSpace(integrationId)
                || !string.Equals(definition.IntegrationId, integrationId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Integration-scoped definitions require a matching integration id.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(integrationId)
                 && definition.Scope != NotificationDefinitionScopes.Platform)
        {
            // Platform-wide definitions may attach integration-scoped subscription rows for preview context.
        }
    }

    private async ValueTask<(string Kind, string RecipientRef)> ResolveRecipientAsync(
        NotificationSubscriptionAddRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ManagedContactInput))
        {
            var managedContactId = await _managedContactProvisioner
                .ResolveOrCreateManagedContactIdAsync(
                    request.ManagedContactInput,
                    request.CreateManagedContactIfMissing,
                    cancellationToken)
                .ConfigureAwait(false);
            return (NotificationRecipientKinds.ManagedContact, managedContactId);
        }

        if (string.IsNullOrWhiteSpace(request.RecipientKind) || string.IsNullOrWhiteSpace(request.RecipientRef))
            throw new InvalidOperationException("Recipient kind and ref, or managedContactInput, is required.");

        return (request.RecipientKind, request.RecipientRef);
    }

    private static void ValidateRecipientKind(string kind)
    {
        if (kind is not (NotificationRecipientKinds.Principal
            or NotificationRecipientKinds.OperationalTeam
            or NotificationRecipientKinds.ManagedContact))
        {
            throw new InvalidOperationException($"Unsupported recipient kind: {kind}.");
        }
    }

    private static void ValidateDeliveryMode(string mode)
    {
        if (mode is not (NotificationDeliveryModes.Immediate
            or NotificationDeliveryModes.Muted
            or NotificationDeliveryModes.DigestOnly
            or NotificationDeliveryModes.ImmediateAndDigest))
        {
            throw new InvalidOperationException($"Unsupported delivery mode: {mode}.");
        }
    }

    private async ValueTask<string?> GetActorIdAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        return actor?.Id.Value;
    }
}
