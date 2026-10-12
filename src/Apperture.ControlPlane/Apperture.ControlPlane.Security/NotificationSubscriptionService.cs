using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class NotificationSubscriptionService : INotificationSubscriptionService
{
    private readonly INotificationSubscriptionStore _subscriptions;
    private readonly INotificationDefinitionStore _definitions;
    private readonly INotificationRecipientResolver _resolver;
    private readonly ManagedContactRecipientProvisioner _managedContactProvisioner;
    private readonly NotificationDefinitionAuthorization _authorization;
    private readonly IPrincipalContext _principalContext;
    private readonly IIntegrationRepository _integrations;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public NotificationSubscriptionService(
        INotificationSubscriptionStore subscriptions,
        INotificationDefinitionStore definitions,
        INotificationRecipientResolver resolver,
        ManagedContactRecipientProvisioner managedContactProvisioner,
        NotificationDefinitionAuthorization authorization,
        IPrincipalContext principalContext,
        IIntegrationRepository integrations,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
    {
        _subscriptions = subscriptions;
        _definitions = definitions;
        _resolver = resolver;
        _managedContactProvisioner = managedContactProvisioner;
        _authorization = authorization;
        _principalContext = principalContext;
        _integrations = integrations;
        _auditedUnitOfWork = auditedUnitOfWork;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new AuditInvariantException("Notification subscription changes require the audited unit of work.");

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

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 6): the subscription, the managed contact it creates when asked to, and their
    /// audit rows commit or roll back together (the contact used to be committed first, and was orphaned by any later failure).
    /// The early read, the management check and the shape checks answer a refused request before a unit opens. Inside the attempt,
    /// in this order: the integration rows (the definition's, then the subscription's own when it names another), the management
    /// check again against the locked integration, the definition row locked (which the definition delete also takes, so no
    /// subscription can attach to a definition that is being deleted), the contact resolved or created under the email lock, the
    /// duplicate check, the row and <c>notification.subscription.added</c>. A recipient that is already subscribed is a refusal
    /// and not a database error.</para>
    /// </remarks>
    public async ValueTask<NotificationSubscription> AddRecipientAsync(
        string definitionId,
        NotificationSubscriptionAddRequest request,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        var definition = await RequireDefinitionAsync(definitionId, manage: true, cancellationToken)
            .ConfigureAwait(false);
        await _authorization
            .RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        ValidateIntegrationScope(definition, request.IntegrationId);
        ValidateDeliveryMode(request.DeliveryMode);
        if (string.IsNullOrWhiteSpace(request.ManagedContactInput))
        {
            if (string.IsNullOrWhiteSpace(request.RecipientKind) || string.IsNullOrWhiteSpace(request.RecipientRef))
                throw new InvalidOperationException("Recipient kind and ref, or managedContactInput, is required.");
            ValidateRecipientKind(request.RecipientKind);

            // A reference is an id (a user, a team or a managed contact). An address typed here would be recorded as the recipient.
            if (request.RecipientRef.Contains('@', StringComparison.Ordinal))
                throw new InvalidOperationException("A recipient reference is an id, not an email address. Use managedContactInput for an address.");
        }

        var subscriptionId = NotificationSubscriptionId.New();
        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.NotificationSubscriptionAdded, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var contextId = await LockIntegrationsAsync(definition.IntegrationId, request.IntegrationId, ct).ConfigureAwait(false);
                await _authorization.RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, ct).ConfigureAwait(false);

                var locked = await _definitions.GetForUpdateAsync(NotificationDefinitionId.From(definitionId), ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Notification definition {definitionId} not found.");

                string kind;
                string recipientRef;
                var contactCreated = false;
                if (!string.IsNullOrWhiteSpace(request.ManagedContactInput))
                {
                    var resolved = await _managedContactProvisioner
                        .ResolveOrCreateAsync(request.ManagedContactInput, request.CreateManagedContactIfMissing, transaction, ct)
                        .ConfigureAwait(false);
                    kind = NotificationRecipientKinds.ManagedContact;
                    recipientRef = resolved.Id;
                    contactCreated = resolved.Created;
                }
                else
                {
                    kind = request.RecipientKind!;
                    recipientRef = request.RecipientRef!;
                }

                ValidateRecipientKind(kind);
                if (await _subscriptions.ExistsAsync(definitionId, request.IntegrationId, kind, recipientRef, ct).ConfigureAwait(false))
                    throw new InvalidOperationException("This recipient is already subscribed to the notification definition.");

                NotificationSubscription added;
                try
                {
                    added = await _subscriptions.AddAsync(
                        new NotificationSubscriptionCreateCommand(
                            definitionId,
                            request.IntegrationId,
                            kind,
                            recipientRef,
                            request.DeliveryMode,
                            NotificationSubscriptionOrigins.AdminAssigned,
                            IsEnabled: true,
                            transaction.Actor.RecordedValue,
                            subscriptionId),
                        ct).ConfigureAwait(false);
                }
                catch (DbUpdateException ex) when (SqlUniqueKeyViolation.IsOnIndex(ex, SubscriptionIndexWithoutIntegration)
                    || SqlUniqueKeyViolation.IsOnIndex(ex, SubscriptionIndexWithIntegration))
                {
                    // The account's own opt-in writes the same platform-wide row without this definition's lock, and committed first.
                    throw new InvalidOperationException("This recipient is already subscribed to the notification definition.");
                }

                await transaction.StageAsync(
                    AuditEvents.NotificationSubscriptionAdded(
                        subscriptionId.Value, definitionId, request.IntegrationId, kind, recipientRef, added.DeliveryMode, contactCreated, contextId),
                    ct).ConfigureAwait(false);
                return added;
            },
            async (committed, ct) => await _subscriptions.GetAsync(subscriptionId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Notification subscription {subscriptionId.Value} could not be read back."),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One audited unit with the locks and re-authorization of <see cref="AddRecipientAsync"/>, plus the subscription row.
    /// A subscription that does not exist, or belongs to another definition, is absent (false) and records nothing. After a lost
    /// acknowledgement a verified commit reports the removal, never "not found".
    /// </remarks>
    public async ValueTask<bool> RemoveAsync(
        string definitionId,
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        var definition = await RequireDefinitionAsync(definitionId, manage: true, cancellationToken)
            .ConfigureAwait(false);
        await _authorization
            .RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        var id = NotificationSubscriptionId.From(subscriptionId);
        var early = await _subscriptions.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (early is null || !string.Equals(early.NotificationDefinitionId, definitionId, StringComparison.Ordinal))
            return false;

        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.NotificationSubscriptionRemoved, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var contextId = await LockIntegrationsAsync(definition.IntegrationId, early.IntegrationId, ct).ConfigureAwait(false);
                await _authorization.RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, ct).ConfigureAwait(false);

                var lockedDefinition = await _definitions.GetForUpdateAsync(NotificationDefinitionId.From(definitionId), ct).ConfigureAwait(false);
                var sub = lockedDefinition is null ? null : await _subscriptions.GetForUpdateAsync(id, ct).ConfigureAwait(false);
                if (sub is null || !string.Equals(sub.NotificationDefinitionId, definitionId, StringComparison.Ordinal))
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                await _subscriptions.RemoveAsync(id, ct).ConfigureAwait(false);
                await transaction.StageAsync(
                    AuditEvents.NotificationSubscriptionRemoved(subscriptionId, definitionId, sub.IntegrationId, sub.RecipientKind, sub.RecipientRef, contextId),
                    ct).ConfigureAwait(false);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One audited unit with the locks of <see cref="RemoveAsync"/>. A request carrying both an enabled flag and a delivery mode
    /// applies only the enabled flag, as before. Setting a value the subscription already has changes nothing and records nothing;
    /// otherwise <c>notification.subscription.updated</c> names the change (enabled, disabled, delivery_mode).
    /// </remarks>
    public async ValueTask<NotificationSubscription?> PatchAsync(
        string definitionId,
        string subscriptionId,
        NotificationSubscriptionPatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        var definition = await RequireDefinitionAsync(definitionId, manage: true, cancellationToken)
            .ConfigureAwait(false);
        await _authorization
            .RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        var id = NotificationSubscriptionId.From(subscriptionId);
        var early = await _subscriptions.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (early is null || !string.Equals(early.NotificationDefinitionId, definitionId, StringComparison.Ordinal))
            return null;

        if (request.IsEnabled is null && !string.IsNullOrWhiteSpace(request.DeliveryMode))
            ValidateDeliveryMode(request.DeliveryMode);

        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.NotificationSubscriptionUpdated, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var contextId = await LockIntegrationsAsync(definition.IntegrationId, early.IntegrationId, ct).ConfigureAwait(false);
                await _authorization.RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, ct).ConfigureAwait(false);

                var lockedDefinition = await _definitions.GetForUpdateAsync(NotificationDefinitionId.From(definitionId), ct).ConfigureAwait(false);
                var sub = lockedDefinition is null ? null : await _subscriptions.GetForUpdateAsync(id, ct).ConfigureAwait(false);
                if (sub is null || !string.Equals(sub.NotificationDefinitionId, definitionId, StringComparison.Ordinal))
                {
                    transaction.DeclareNoOp();
                    return null;
                }

                if (request.IsEnabled is { } enabled)
                {
                    if (enabled == sub.IsEnabled)
                    {
                        transaction.DeclareNoOp();
                        return sub;
                    }

                    var updated = await _subscriptions.SetEnabledAsync(id, enabled, transaction.Actor.RecordedValue, ct).ConfigureAwait(false);
                    await transaction.StageAsync(
                        AuditEvents.NotificationSubscriptionUpdated(
                            subscriptionId, definitionId, sub.IntegrationId, enabled ? "enabled" : "disabled", deliveryMode: null, contextId),
                        ct).ConfigureAwait(false);
                    return updated;
                }

                if (!string.IsNullOrWhiteSpace(request.DeliveryMode))
                {
                    if (string.Equals(request.DeliveryMode, sub.DeliveryMode, StringComparison.Ordinal))
                    {
                        transaction.DeclareNoOp();
                        return sub;
                    }

                    var updated = await _subscriptions
                        .SetDeliveryModeAsync(id, request.DeliveryMode, transaction.Actor.RecordedValue, ct)
                        .ConfigureAwait(false);
                    await transaction.StageAsync(
                        AuditEvents.NotificationSubscriptionUpdated(
                            subscriptionId, definitionId, sub.IntegrationId, "delivery_mode", request.DeliveryMode, contextId),
                        ct).ConfigureAwait(false);
                    return updated;
                }

                transaction.DeclareNoOp();
                return sub;
            },
            async (committed, ct) => await _subscriptions.GetAsync(id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Notification subscription {subscriptionId} could not be read back."),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One audited unit with the locks of <see cref="AddRecipientAsync"/> (the definition's integration and the definition row). An
    /// opt-out that already exists is returned unchanged and records nothing; otherwise <c>notification.subscription.user_opt_out_set</c>
    /// targets the new opt-out row and names the user by id.
    /// </remarks>
    public async ValueTask<NotificationSubscriptionUserOverride> SetUserOptOutAsync(
        string definitionId,
        NotificationUserOptOutRequest request,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        var definition = await RequireDefinitionAsync(definitionId, manage: true, cancellationToken)
            .ConfigureAwait(false);
        await _authorization
            .RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.UserId))
            throw new InvalidOperationException("User id is required.");

        var overrideId = $"noverride_{Guid.NewGuid():N}";
        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.NotificationSubscriptionUserOptOutSet, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var contextId = await LockIntegrationsAsync(definition.IntegrationId, null, ct).ConfigureAwait(false);
                await _authorization.RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, ct).ConfigureAwait(false);

                _ = await _definitions.GetForUpdateAsync(NotificationDefinitionId.From(definitionId), ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Notification definition {definitionId} not found.");

                var existing = await _subscriptions.FindUserOptOutAsync(request.UserId, definitionId, request.OperationalTeamId, ct).ConfigureAwait(false);
                if (existing is not null)
                {
                    transaction.DeclareNoOp();
                    return existing;
                }

                var created = await _subscriptions.SetUserOptOutAsync(
                    new NotificationSubscriptionUserOverrideCommand(
                        request.UserId, definitionId, request.OperationalTeamId, transaction.Actor.RecordedValue, overrideId),
                    ct).ConfigureAwait(false);
                await transaction.StageAsync(
                    AuditEvents.NotificationUserOptOutSet(overrideId, definitionId, request.UserId, request.OperationalTeamId, contextId),
                    ct).ConfigureAwait(false);
                return created;
            },
            async (committed, ct) => await _subscriptions.FindUserOptOutAsync(request.UserId, definitionId, request.OperationalTeamId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The opt-out could not be read back."),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One audited unit with the locks of <see cref="SetUserOptOutAsync"/>. Removing an opt-out that does not exist is false and
    /// records nothing; after a lost acknowledgement a verified commit reports the removal.
    /// </remarks>
    public async ValueTask<bool> RemoveUserOptOutAsync(
        string definitionId,
        NotificationUserOptOutRequest request,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        var definition = await RequireDefinitionAsync(definitionId, manage: true, cancellationToken)
            .ConfigureAwait(false);
        await _authorization
            .RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.UserId))
            throw new InvalidOperationException("User id is required.");

        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.NotificationSubscriptionUserOptOutRemoved, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var contextId = await LockIntegrationsAsync(definition.IntegrationId, null, ct).ConfigureAwait(false);
                await _authorization.RequireDefinitionAccessAsync(definition.IntegrationId, manage: true, ct).ConfigureAwait(false);

                var lockedDefinition = await _definitions.GetForUpdateAsync(NotificationDefinitionId.From(definitionId), ct).ConfigureAwait(false);
                var existing = lockedDefinition is null
                    ? null
                    : await _subscriptions.FindUserOptOutAsync(request.UserId, definitionId, request.OperationalTeamId, ct).ConfigureAwait(false);
                if (existing is null)
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                await _subscriptions.RemoveUserOptOutAsync(request.UserId, definitionId, request.OperationalTeamId, ct).ConfigureAwait(false);
                await transaction.StageAsync(
                    AuditEvents.NotificationUserOptOutRemoved(existing.Id, definitionId, request.UserId, request.OperationalTeamId, contextId),
                    ct).ConfigureAwait(false);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);
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

    private const string SubscriptionIndexWithoutIntegration = "IX_NotificationSubscriptions_NotificationDefinitionId_RecipientKind_RecipientRef";
    private const string SubscriptionIndexWithIntegration = "IX_NotificationSubscriptions_NotificationDefinitionId_IntegrationId_RecipientKind_RecipientRef";

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

    /// <summary>
    /// Locks the integration rows a subscription writer depends on, in a fixed order (the definition's integration first, then the
    /// subscription's own when it names another), requiring each to exist, and returns the resource context to record: the owner of
    /// the definition's integration, or of the subscription's own. An integration delete holds the same row, so none can be
    /// deleted between a writer's check and its commit.
    /// </summary>
    private async ValueTask<string?> LockIntegrationsAsync(string? definitionIntegrationId, string? subscriptionIntegrationId, CancellationToken cancellationToken)
    {
        string? contextId = null;
        if (!string.IsNullOrWhiteSpace(definitionIntegrationId))
        {
            var locked = await _integrations.GetByIdForUpdateAsync(new IntegrationId(definitionIntegrationId), cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Integration {definitionIntegrationId} not found.");
            contextId = locked.ResourceContextId.Value;
        }

        if (!string.IsNullOrWhiteSpace(subscriptionIntegrationId)
            && !string.Equals(subscriptionIntegrationId, definitionIntegrationId, StringComparison.Ordinal))
        {
            var locked = await _integrations.GetByIdForUpdateAsync(new IntegrationId(subscriptionIntegrationId), cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Integration {subscriptionIntegrationId} not found.");
            contextId = locked.ResourceContextId.Value;
        }

        return contextId;
    }
}
