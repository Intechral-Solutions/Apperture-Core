using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Client;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Authorized read surface for client definitions. Reuses connection.read. Epic 51.
/// </summary>
public sealed class ClientDefinitionReadService : IClientDefinitionReadService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorization;
    private readonly IClientDefinitionQueryService _queryService;

    public ClientDefinitionReadService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorization,
        IClientDefinitionQueryService queryService)
    {
        _principalContext = principalContext;
        _authorization = authorization;
        _queryService = queryService;
    }

    /// <summary>
    /// Lists all client definitions. Requires connection.read at instance scope.
    /// </summary>
    public async ValueTask<IReadOnlyList<ClientDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCanViewAsync(cancellationToken).ConfigureAwait(false);
        return await _queryService.GetAllAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a client definition by id. Requires connection.read at instance scope.
    /// </summary>
    public async ValueTask<ClientDefinition?> GetByIdAsync(ClientDefinitionId id, CancellationToken cancellationToken = default)
    {
        await EnsureCanViewAsync(cancellationToken).ConfigureAwait(false);
        return await _queryService.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureCanViewAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var allowed = await _authorization.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ConnectionRead, new ResourceDescriptor(ResourceTypeConstants.Connection, ScopeKeyConstants.Instance)),
                null),
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to view client definitions.");
    }
}
