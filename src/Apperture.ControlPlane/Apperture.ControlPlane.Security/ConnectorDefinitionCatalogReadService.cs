using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Authorized catalog read using <c>connector.definition.read</c> on <see cref="ResourceTypeConstants.ConnectorDefinition"/> at instance scope.
/// Lists definitions from <see cref="IConnectorDefinitionRegistry"/> only.
/// </summary>
public sealed class ConnectorDefinitionCatalogReadService : IConnectorDefinitionCatalogReadService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorization;
    private readonly IConnectorDefinitionRegistry _registry;

    public ConnectorDefinitionCatalogReadService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorization,
        IConnectorDefinitionRegistry registry)
    {
        _principalContext = principalContext;
        _authorization = authorization;
        _registry = registry;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ConnectorDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCanViewAsync(cancellationToken).ConfigureAwait(false);
        return _registry.GetAll();
    }

    /// <inheritdoc />
    public async ValueTask<ConnectorDefinition?> GetByIdAsync(
        ConnectorDefinitionId id,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanViewAsync(cancellationToken).ConfigureAwait(false);
        return _registry.Get(id);
    }

    private async Task EnsureCanViewAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var allowed = await _authorization.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(
                    ActionConstants.ConnectorDefinitionRead,
                    new ResourceDescriptor(ResourceTypeConstants.ConnectorDefinition, ScopeKeyConstants.Instance)),
                null),
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to view connector definitions.");
    }
}
