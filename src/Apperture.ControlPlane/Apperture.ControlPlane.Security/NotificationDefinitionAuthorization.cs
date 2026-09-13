using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

public sealed class NotificationDefinitionAuthorization
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;
    private readonly IAuthorizationEvaluationService _authEval;
    private readonly IIntegrationRepository _integrations;

    public NotificationDefinitionAuthorization(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IAuthorizationEvaluationService authEval,
        IIntegrationRepository integrations)
    {
        _principalContext = principalContext;
        _auth = auth;
        _authEval = authEval;
        _integrations = integrations;
    }

    public async ValueTask<bool> CanViewDefinitionsAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return false;

        return await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(
                ActionConstants.NotificationDefinitionRead,
                new ResourceDescriptor(ResourceTypeConstants.NotificationDefinition, ScopeKeyConstants.Instance)),
            null,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RequirePlatformReadAsync(CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        await RequireInstancePermissionAsync(actor, ActionConstants.NotificationDefinitionRead, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask RequirePlatformManageAsync(CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        await RequireInstancePermissionAsync(actor, ActionConstants.NotificationDefinitionManage, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask RequireIntegrationReadAsync(string integrationId, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        await RequireInstancePermissionAsync(actor, ActionConstants.NotificationDefinitionRead, cancellationToken)
            .ConfigureAwait(false);
        await RequireIntegrationPermissionAsync(actor, integrationId, ActionConstants.IntegrationRead, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask RequireIntegrationManageAsync(string integrationId, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        await RequireInstancePermissionAsync(actor, ActionConstants.NotificationDefinitionManage, cancellationToken)
            .ConfigureAwait(false);
        await RequireIntegrationPermissionAsync(actor, integrationId, ActionConstants.IntegrationUpdate, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask RequireDefinitionAccessAsync(
        string? integrationId,
        bool manage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(integrationId))
        {
            if (manage)
                await RequirePlatformManageAsync(cancellationToken).ConfigureAwait(false);
            else
                await RequirePlatformReadAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (manage)
            await RequireIntegrationManageAsync(integrationId, cancellationToken).ConfigureAwait(false);
        else
            await RequireIntegrationReadAsync(integrationId, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IActor> RequireActorAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");
        return actor;
    }

    private async ValueTask RequireInstancePermissionAsync(
        IActor actor,
        string action,
        CancellationToken cancellationToken)
    {
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(
                action,
                new ResourceDescriptor(ResourceTypeConstants.NotificationDefinition, ScopeKeyConstants.Instance)),
            null,
            null,
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException($"Not authorized: {action}.");
    }

    private async ValueTask RequireIntegrationPermissionAsync(
        IActor actor,
        string integrationId,
        string action,
        CancellationToken cancellationToken)
    {
        var integration = await _integrations.GetByIdAsync(new IntegrationId(integrationId), cancellationToken)
            .ConfigureAwait(false);
        if (integration is null)
            throw new InvalidOperationException($"Integration {integrationId} not found.");

        var identity = integration.GetIdentity();
        var allowed = await _authEval.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(action, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity),
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException($"Not authorized: {action} on integration {integrationId}.");
    }
}
