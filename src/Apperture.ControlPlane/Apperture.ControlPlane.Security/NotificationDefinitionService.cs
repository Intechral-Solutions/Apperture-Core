using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.Shared.Kernel;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.ControlPlane.Security;

public sealed class NotificationDefinitionService : INotificationDefinitionService
{
    private readonly INotificationDefinitionStore _store;
    private readonly INotificationRuleValidator _ruleValidator;
    private readonly INotificationTemplateValidator _templateValidator;
    private readonly INotificationTemplateRenderer _renderer;
    private readonly INotificationRenderContextFactory _contextFactory;
    private readonly NotificationDefinitionAuthorization _authorization;
    private readonly IPrincipalContext _principalContext;
    private readonly IIntegrationRepository _integrations;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public NotificationDefinitionService(
        INotificationDefinitionStore store,
        INotificationRuleValidator ruleValidator,
        INotificationTemplateValidator templateValidator,
        INotificationTemplateRenderer renderer,
        INotificationRenderContextFactory contextFactory,
        NotificationDefinitionAuthorization authorization,
        IPrincipalContext principalContext,
        IIntegrationRepository integrations,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
    {
        _store = store;
        _ruleValidator = ruleValidator;
        _templateValidator = templateValidator;
        _renderer = renderer;
        _contextFactory = contextFactory;
        _authorization = authorization;
        _principalContext = principalContext;
        _integrations = integrations;
        _auditedUnitOfWork = auditedUnitOfWork;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new AuditInvariantException("Notification definition changes require the audited unit of work.");

    public async ValueTask<IReadOnlyList<NotificationDefinition>> ListAsync(
        NotificationDefinitionListQuery query,
        CancellationToken cancellationToken = default)
    {
        var effectiveQuery = query;
        if (!string.IsNullOrWhiteSpace(query.IntegrationId))
        {
            await _authorization.RequireIntegrationReadAsync(query.IntegrationId, cancellationToken)
                .ConfigureAwait(false);
            var integration = await _integrations.GetByIdAsync(new IntegrationId(query.IntegrationId), cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Integration {query.IntegrationId} not found.");
            effectiveQuery = query with { ApplicableModuleId = integration.ModuleId };
        }
        else
            await _authorization.RequirePlatformReadAsync(cancellationToken).ConfigureAwait(false);

        return await _store.ListAsync(effectiveQuery, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<NotificationDefinition?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var row = await _store.GetAsync(NotificationDefinitionId.From(id), cancellationToken).ConfigureAwait(false);
        if (row is null)
            return null;

        await _authorization.RequireDefinitionAccessAsync(row.IntegrationId, manage: false, cancellationToken)
            .ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 6). Request validation and the early authorization answer a refused request before a
    /// unit opens; they decide nothing. Inside the attempt, in this order: the integration row (locked to the commit, which keeps an
    /// integration delete from counting its blockers before this definition is visible and an ownership move from changing the
    /// governed identity), the authorization again against the integration as locked, the display-name check (every writer of an
    /// integration's definition names takes the same row, so the check cannot race), the row and <c>notification.definition.created</c>.
    /// The identity is minted before the unit, so a verified commit after a lost acknowledgement is reconstructed from the row.</para>
    /// </remarks>
    public async ValueTask<NotificationDefinition> CreateAsync(
        NotificationDefinitionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        await _authorization.RequireIntegrationManageAsync(request.IntegrationId, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new InvalidOperationException("Display name is required.");

        var eventTypes = ExtractEventTypes(request.RuleJson);
        var validation = ValidateDocuments(
            request.RuleJson,
            request.TemplateJson,
            NotificationDefinitionOrigins.UserCreated,
            request.IntegrationId,
            eventTypes);
        if (!validation.IsValid)
            throw new InvalidOperationException(FormatValidationErrors(validation));

        var id = NotificationDefinitionId.New();
        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.NotificationDefinitionCreated, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var integration = await _integrations.GetByIdForUpdateAsync(new IntegrationId(request.IntegrationId), ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Integration {request.IntegrationId} not found.");

                // The integration row is fixed until the commit, so this sees any ownership move that committed first.
                await _authorization.RequireIntegrationManageAsync(request.IntegrationId, ct).ConfigureAwait(false);

                if (await _store.ExistsDisplayNameAsync(
                        NotificationDefinitionScopes.Integration, request.IntegrationId, request.DisplayName, excludingId: null, ct).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("A notification definition with this display name already exists.");
                }

                var created = await _store.CreateUserDefinitionAsync(
                    new NotificationDefinitionCreateCommand(
                        request.DisplayName,
                        request.Description,
                        request.IntegrationId,
                        request.RuleJson,
                        request.TemplateJson,
                        request.IsEnabled,
                        request.DefaultSeverity,
                        transaction.Actor.RecordedValue,
                        request.ToastEnabledByDefault,
                        id),
                    ct).ConfigureAwait(false);

                await transaction.StageAsync(
                    AuditEvents.NotificationDefinitionCreated(
                        id.Value, created.DisplayName, request.IntegrationId, created.IsEnabled, created.ToastEnabledByDefault,
                        created.DefaultSeverity, created.PrimaryEventType, integration.ResourceContextId.Value),
                    ct).ConfigureAwait(false);
                return created;
            },
            async (committed, ct) => await _store.GetAsync(id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Notification definition {id.Value} could not be read back."),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit.</b> An early read and the management check answer a missing or forbidden request. Inside the
    /// attempt: the integration row (an integration-scoped definition only), the authorization again, the definition row locked, the
    /// documents validated against the row as locked, the change computed against it, then the update and
    /// <c>notification.definition.updated</c> naming the parts that changed (never their content). A request that changes nothing
    /// declares a no-op and records nothing. A rename is checked against the integration's other definitions, so a collision is a
    /// refusal and not a database error. A definition that was deleted meanwhile is reported as absent.</para>
    /// </remarks>
    public async ValueTask<NotificationDefinition?> UpdateAsync(
        string id,
        NotificationDefinitionUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        var definitionId = NotificationDefinitionId.From(id);
        var early = await _store.GetAsync(definitionId, cancellationToken).ConfigureAwait(false);
        if (early is null)
            return null;

        await _authorization.RequireDefinitionAccessAsync(early.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.NotificationDefinitionUpdated, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var integration = await LockIntegrationAsync(early.IntegrationId, ct).ConfigureAwait(false);
                await _authorization.RequireDefinitionAccessAsync(early.IntegrationId, manage: true, ct).ConfigureAwait(false);

                var locked = await _store.GetForUpdateAsync(definitionId, ct).ConfigureAwait(false);
                if (locked is null)
                {
                    transaction.DeclareNoOp();
                    return null;
                }

                var ruleJson = request.RuleJson ?? locked.RuleJson;
                var templateJson = request.TemplateJson ?? locked.TemplateJson;
                var validation = ValidateDocuments(ruleJson, templateJson, locked.Origin, locked.IntegrationId, ExtractEventTypes(ruleJson));
                if (!validation.IsValid)
                    throw new InvalidOperationException(FormatValidationErrors(validation));

                if (IsBuiltIn(locked) && request.RuleJson is not null)
                    throw new InvalidOperationException("Built-in rule JSON is immutable.");
                if (IsBuiltIn(locked) && request.TemplateJson is not null)
                    throw new InvalidOperationException("Built-in template JSON is immutable.");

                var changed = new List<string>();
                if (request.DisplayName is not null && !string.Equals(request.DisplayName, locked.DisplayName, StringComparison.Ordinal))
                    changed.Add("displayName");
                if (request.Description is not null && !string.Equals(request.Description, locked.Description, StringComparison.Ordinal))
                    changed.Add("description");
                if (request.RuleJson is not null && !string.Equals(request.RuleJson, locked.RuleJson, StringComparison.Ordinal))
                    changed.Add("rule");
                if (request.TemplateJson is not null && !string.Equals(request.TemplateJson, locked.TemplateJson, StringComparison.Ordinal))
                    changed.Add("template");
                if (request.IsEnabled is { } enabled && enabled != locked.IsEnabled)
                    changed.Add("enabled");
                if (request.DefaultSeverity is not null && !string.Equals(request.DefaultSeverity, locked.DefaultSeverity, StringComparison.Ordinal))
                    changed.Add("severity");
                if (request.ToastEnabledByDefault is { } toast && toast != locked.ToastEnabledByDefault)
                    changed.Add("toastDefault");

                if (changed.Count == 0)
                {
                    transaction.DeclareNoOp();
                    return locked;
                }

                if (changed.Contains("displayName") && string.Equals(locked.Origin, NotificationDefinitionOrigins.UserCreated, StringComparison.Ordinal)
                    && await _store.ExistsDisplayNameAsync(locked.Scope, locked.IntegrationId, request.DisplayName!, definitionId, ct).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("A notification definition with this display name already exists.");
                }

                var updated = await _store.UpdateAsync(
                    definitionId,
                    new NotificationDefinitionUpdateCommand(
                        request.DisplayName,
                        request.Description,
                        request.RuleJson,
                        request.TemplateJson,
                        request.IsEnabled,
                        request.DefaultSeverity,
                        transaction.Actor.RecordedValue,
                        request.ToastEnabledByDefault),
                    ct).ConfigureAwait(false);
                if (updated is null)
                    throw new InvalidOperationException($"Notification definition {id} disappeared while it was locked.");

                changed.Sort(StringComparer.Ordinal);
                await transaction.StageAsync(
                    AuditEvents.NotificationDefinitionUpdated(
                        id, updated.DisplayName, locked.IntegrationId, changed, integration?.ResourceContextId.Value),
                    ct).ConfigureAwait(false);
                return updated;
            },
            async (committed, ct) => await _store.GetAsync(definitionId, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One audited unit with the locks and re-authorization of <see cref="UpdateAsync"/>. Setting the state a definition already has
    /// changes nothing and records nothing; otherwise the row is <c>notification.definition.enabled</c> or <c>.disabled</c>.
    /// </remarks>
    public async ValueTask<NotificationDefinition?> SetEnabledAsync(
        string id,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        var definitionId = NotificationDefinitionId.From(id);
        var early = await _store.GetAsync(definitionId, cancellationToken).ConfigureAwait(false);
        if (early is null)
            return null;

        await _authorization.RequireDefinitionAccessAsync(early.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        return await unit.ExecuteAsync(
            new AuditedOperation(
                AuditActionNames.NotificationDefinitionEnabled,
                AuditActor.CurrentPrincipal,
                AuditActionNames.NotificationDefinitionEnableActions),
            async (transaction, ct) =>
            {
                var integration = await LockIntegrationAsync(early.IntegrationId, ct).ConfigureAwait(false);
                await _authorization.RequireDefinitionAccessAsync(early.IntegrationId, manage: true, ct).ConfigureAwait(false);

                var locked = await _store.GetForUpdateAsync(definitionId, ct).ConfigureAwait(false);
                if (locked is null || locked.IsEnabled == isEnabled)
                {
                    transaction.DeclareNoOp();
                    return locked;
                }

                var updated = await _store.SetEnabledAsync(definitionId, isEnabled, transaction.Actor.RecordedValue, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Notification definition {id} disappeared while it was locked.");
                await transaction.StageAsync(
                    AuditEvents.NotificationDefinitionEnableChanged(id, updated.DisplayName, locked.IntegrationId, isEnabled, integration?.ResourceContextId.Value),
                    ct).ConfigureAwait(false);
                return updated;
            },
            async (committed, ct) => await _store.GetAsync(definitionId, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b>, the definition delete (a W1 delete: the row, its subscriptions, user opt-outs and per-user
    /// preferences, and the audit row commit together). Inside the attempt: the integration row (integration-scoped definitions),
    /// the authorization again, the definition row locked (which a subscription writer also takes, so none can attach to a
    /// definition that is being deleted), the built-in refusal, the cascade with its counts, and
    /// <c>notification.definition.deleted</c> naming the definition and counting what went with it. A definition deleted meanwhile
    /// changes nothing and records nothing. After a lost acknowledgement a verified commit reports success, never "not found".</para>
    /// </remarks>
    public async ValueTask<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        var definitionId = NotificationDefinitionId.From(id);
        var early = await _store.GetAsync(definitionId, cancellationToken).ConfigureAwait(false);
        if (early is null)
            return false;

        // Same gate as create/update. No new permission is introduced by CTX-3B.
        await _authorization.RequireDefinitionAccessAsync(early.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.NotificationDefinitionDeleted, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var integration = await LockIntegrationAsync(early.IntegrationId, ct).ConfigureAwait(false);
                await _authorization.RequireDefinitionAccessAsync(early.IntegrationId, manage: true, ct).ConfigureAwait(false);

                var locked = await _store.GetForUpdateAsync(definitionId, ct).ConfigureAwait(false);
                if (locked is null)
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                if (IsBuiltIn(locked))
                    throw new InvalidOperationException("Built-in notification definitions cannot be deleted.");

                var removed = await _store.DeleteWithCountsAsync(definitionId, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Notification definition {id} disappeared while it was locked.");
                await transaction.StageAsync(
                    AuditEvents.NotificationDefinitionDeleted(
                        id, locked.DisplayName, locked.IntegrationId, removed.SubscriptionsRemoved, removed.UserOptOutsRemoved,
                        removed.UserPreferencesRemoved, integration?.ResourceContextId.Value),
                    ct).ConfigureAwait(false);
                return true;
            },
            // A verified commit is the deletion having happened, whatever the row now says.
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsBuiltIn(NotificationDefinition definition) =>
        definition.Origin is NotificationDefinitionOrigins.PlatformBuiltIn or NotificationDefinitionOrigins.ModuleBuiltIn;

    /// <summary>Locks the owning integration row of an integration-scoped definition; null for a platform- or module-scoped one.</summary>
    private async ValueTask<DomainIntegration?> LockIntegrationAsync(string? integrationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(integrationId))
            return null;
        return await _integrations.GetByIdForUpdateAsync(new IntegrationId(integrationId), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<NotificationValidationResult> ValidateAsync(
        NotificationDefinitionValidateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.OwnerIntegrationId))
            await _authorization.RequireIntegrationManageAsync(request.OwnerIntegrationId, cancellationToken)
                .ConfigureAwait(false);
        else
            await _authorization.RequirePlatformManageAsync(cancellationToken).ConfigureAwait(false);

        return ValidateDocuments(
            request.RuleJson,
            request.TemplateJson,
            request.Origin ?? NotificationDefinitionOrigins.UserCreated,
            request.OwnerIntegrationId,
            request.EventTypes ?? ExtractEventTypes(request.RuleJson));
    }

    public async ValueTask<RenderedNotificationSnapshot> RenderPreviewAsync(
        string? definitionId,
        NotificationDefinitionRenderPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        string templateJson;
        if (!string.IsNullOrWhiteSpace(definitionId))
        {
            var def = await GetAsync(definitionId, cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException("Definition not found.");
            templateJson = request.TemplateJson ?? def.TemplateJson;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.Sample.IntegrationId))
            {
                await _authorization.RequireIntegrationManageAsync(request.Sample.IntegrationId, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _authorization.RequirePlatformReadAsync(cancellationToken).ConfigureAwait(false);
            }

            templateJson = request.TemplateJson
                           ?? throw new InvalidOperationException("templateJson is required when definition id is omitted.");
        }

        if (!NotificationDocumentJson.TryDeserializeTemplate(templateJson, out var template, out var error))
            throw new InvalidOperationException(error ?? "Invalid template JSON.");

        var context = _contextFactory.FromSample(request.Sample);
        return _renderer.Render(template!, context, request.VariantKey);
    }

    private NotificationValidationResult ValidateDocuments(
        string ruleJson,
        string templateJson,
        string origin,
        string? ownerIntegrationId,
        IReadOnlyList<string> eventTypes)
    {
        var rule = _ruleValidator.Validate(
            ruleJson,
            new NotificationRuleValidationContext(origin, ownerIntegrationId, eventTypes));
        var template = _templateValidator.Validate(
            templateJson,
            new NotificationTemplateValidationContext(eventTypes));
        return NotificationValidationResult.Merge(rule, template);
    }

    private static IReadOnlyList<string> ExtractEventTypes(string ruleJson)
    {
        if (!NotificationDocumentJson.TryDeserializeRule(ruleJson, out var doc, out _))
            return [];
        return doc?.Trigger?.EventTypes?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [];
    }

    public ValueTask<IReadOnlyDictionary<string, string>> GetIntegrationOwnerContextIdsAsync(
        IReadOnlyCollection<string> integrationIds,
        CancellationToken cancellationToken = default) =>
        _store.GetIntegrationOwnerContextIdsAsync(integrationIds, cancellationToken);

    private static string FormatValidationErrors(NotificationValidationResult result) =>
        string.Join("; ", result.Issues.Select(i => i.Message));
}
