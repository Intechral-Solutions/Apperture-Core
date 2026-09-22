using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.Shared.Kernel;

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
    private readonly IAuditService _audit;

    public NotificationDefinitionService(
        INotificationDefinitionStore store,
        INotificationRuleValidator ruleValidator,
        INotificationTemplateValidator templateValidator,
        INotificationTemplateRenderer renderer,
        INotificationRenderContextFactory contextFactory,
        NotificationDefinitionAuthorization authorization,
        IPrincipalContext principalContext,
        IIntegrationRepository integrations,
        IAuditService audit)
    {
        _store = store;
        _ruleValidator = ruleValidator;
        _templateValidator = templateValidator;
        _renderer = renderer;
        _contextFactory = contextFactory;
        _authorization = authorization;
        _principalContext = principalContext;
        _integrations = integrations;
        _audit = audit;
    }

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

    public async ValueTask<NotificationDefinition> CreateAsync(
        NotificationDefinitionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        await _authorization.RequireIntegrationManageAsync(request.IntegrationId, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new InvalidOperationException("Display name is required.");

        if (await _store.ExistsDisplayNameAsync(
                NotificationDefinitionScopes.Integration,
                request.IntegrationId,
                request.DisplayName,
                excludingId: null,
                cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("A notification definition with this display name already exists.");
        }

        var eventTypes = ExtractEventTypes(request.RuleJson);
        var validation = ValidateDocuments(
            request.RuleJson,
            request.TemplateJson,
            NotificationDefinitionOrigins.UserCreated,
            request.IntegrationId,
            eventTypes);
        if (!validation.IsValid)
            throw new InvalidOperationException(FormatValidationErrors(validation));

        var actorId = await GetActorIdAsync(cancellationToken).ConfigureAwait(false);
        return await _store.CreateUserDefinitionAsync(
            new NotificationDefinitionCreateCommand(
                request.DisplayName,
                request.Description,
                request.IntegrationId,
                request.RuleJson,
                request.TemplateJson,
                request.IsEnabled,
                request.DefaultSeverity,
                actorId,
                request.ToastEnabledByDefault),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<NotificationDefinition?> UpdateAsync(
        string id,
        NotificationDefinitionUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = await _store.GetAsync(NotificationDefinitionId.From(id), cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return null;

        await _authorization.RequireDefinitionAccessAsync(existing.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        var ruleJson = request.RuleJson ?? existing.RuleJson;
        var templateJson = request.TemplateJson ?? existing.TemplateJson;
        var eventTypes = ExtractEventTypes(ruleJson);
        var validation = ValidateDocuments(
            ruleJson,
            templateJson,
            existing.Origin,
            existing.IntegrationId,
            eventTypes);
        if (!validation.IsValid)
            throw new InvalidOperationException(FormatValidationErrors(validation));

        var actorId = await GetActorIdAsync(cancellationToken).ConfigureAwait(false);
        return await _store.UpdateAsync(
            NotificationDefinitionId.From(id),
            new NotificationDefinitionUpdateCommand(
                request.DisplayName,
                request.Description,
                request.RuleJson,
                request.TemplateJson,
                request.IsEnabled,
                request.DefaultSeverity,
                actorId,
                request.ToastEnabledByDefault),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<NotificationDefinition?> SetEnabledAsync(
        string id,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        var existing = await _store.GetAsync(NotificationDefinitionId.From(id), cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return null;

        await _authorization.RequireDefinitionAccessAsync(existing.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        var actorId = await GetActorIdAsync(cancellationToken).ConfigureAwait(false);
        return await _store.SetEnabledAsync(NotificationDefinitionId.From(id), isEnabled, actorId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var existing = await _store.GetAsync(NotificationDefinitionId.From(id), cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return false;

        // Same gate as create/update. No new permission is introduced by CTX-3B.
        await _authorization.RequireDefinitionAccessAsync(existing.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        var deleted = await _store.DeleteAsync(NotificationDefinitionId.From(id), cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            // NotificationDefinitionService has no pre-existing audit convention (CreateAsync/UpdateAsync/
            // SetEnabledAsync do not audit), so deletion follows the general house AuditRecord pattern used
            // elsewhere for "one audit record per destructive/mutating operation" (e.g.
            // ResourceContextManagementService, IntegrationSettingsService) rather than inventing one.
            await AppendDeleteAuditAsync(id, existing, cancellationToken).ConfigureAwait(false);
        }
        return deleted;
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

    private async ValueTask<string?> GetActorIdAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        return actor?.Id.Value;
    }

    private async ValueTask AppendDeleteAuditAsync(string id, NotificationDefinition existing, CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);

        string? resourceContextId = null;
        if (!string.IsNullOrWhiteSpace(existing.IntegrationId))
        {
            var integration = await _integrations.GetByIdAsync(new IntegrationId(existing.IntegrationId), cancellationToken)
                .ConfigureAwait(false);
            resourceContextId = integration?.ResourceContextId.Value;
        }

        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actor?.Id ?? new ActorId("system"),
            ActionConstants.NotificationDefinitionDeleted,
            new ResourceDescriptor(ResourceTypeConstants.NotificationDefinition, id),
            Outcome: "succeeded",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            Details: $"Deleted notification definition '{existing.DisplayName}' ({id})",
            ResourceContextId: resourceContextId);
        await _audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyDictionary<string, string>> GetIntegrationOwnerContextIdsAsync(
        IReadOnlyCollection<string> integrationIds,
        CancellationToken cancellationToken = default) =>
        _store.GetIntegrationOwnerContextIdsAsync(integrationIds, cancellationToken);

    private static string FormatValidationErrors(NotificationValidationResult result) =>
        string.Join("; ", result.Issues.Select(i => i.Message));
}
