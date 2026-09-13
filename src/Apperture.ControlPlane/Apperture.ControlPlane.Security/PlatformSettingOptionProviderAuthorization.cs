using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Authorization for provider-backed platform setting option lookup and write-time validation.
/// </summary>
public sealed class PlatformSettingOptionProviderAuthorization
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IIntegrationRepository _integrations;

    public PlatformSettingOptionProviderAuthorization(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IIntegrationRepository integrations)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _integrations = integrations;
    }

    public async ValueTask<IActor> RequireReadAsync(string? integrationId, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        await RequireInstancePermissionAsync(actor, ActionConstants.PlatformSettingsRead, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(integrationId))
            await RequireIntegrationPermissionAsync(actor, integrationId, ActionConstants.IntegrationRead, cancellationToken)
                .ConfigureAwait(false);

        return actor;
    }

    public async ValueTask<IActor> RequireManageAsync(string? integrationId, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        await RequireInstancePermissionAsync(actor, ActionConstants.PlatformSettingsManage, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(integrationId))
            await RequireIntegrationPermissionAsync(actor, integrationId, ActionConstants.IntegrationRead, cancellationToken)
                .ConfigureAwait(false);

        return actor;
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
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(
                    action,
                    new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance)),
                new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance)),
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
            throw new KeyNotFoundException($"Integration {integrationId} not found.");

        var identity = integration.GetIdentity();
        var allowed = await _authorizationEvaluation.EvaluateAsync(
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
