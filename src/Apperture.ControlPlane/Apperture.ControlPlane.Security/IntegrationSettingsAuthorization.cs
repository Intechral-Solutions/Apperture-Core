using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>Authorization for integration-scoped settings API (integration.read / integration.update only).</summary>
public sealed class IntegrationSettingsAuthorization
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IIntegrationRepository _integrations;

    public IntegrationSettingsAuthorization(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IIntegrationRepository integrations)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _integrations = integrations;
    }

    public async ValueTask<IActor> RequireIntegrationReadAsync(string integrationId, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        await RequireIntegrationPermissionAsync(actor, integrationId, ActionConstants.IntegrationRead, cancellationToken)
            .ConfigureAwait(false);
        return actor;
    }

    public async ValueTask<IActor> RequireIntegrationManageAsync(string integrationId, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        await RequireIntegrationPermissionAsync(actor, integrationId, ActionConstants.IntegrationUpdate, cancellationToken)
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
