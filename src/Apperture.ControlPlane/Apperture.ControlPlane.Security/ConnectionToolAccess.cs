using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Authorized, local-only access to a Connection's tools. It depends on the Connection repository, authorization and the
/// providers that connector packages registered; it deliberately depends on no client factory, connection resolver or secret
/// resolver, so answering "which tools?" cannot create a remote client, resolve a secret or touch the network.
/// </summary>
public sealed class ConnectionToolAccess : IConnectionToolAccess
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IReadOnlyList<IConnectionToolProvider> _providers;

    public ConnectionToolAccess(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IConnectionRepository connectionRepository,
        IEnumerable<IConnectionToolProvider> providers)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _connectionRepository = connectionRepository;
        _providers = providers.ToList();
    }

    /// <inheritdoc />
    public async ValueTask<ConnectionToolSet?> GetToolsAsync(ConnectionId id, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No authenticated actor.");

        var connection = await _connectionRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (connection is null)
            return null;

        await AuthorizeAsync(actor, connection, cancellationToken).ConfigureAwait(false);

        // A retired Connection is excluded from binding eligibility and must not drive live operations.
        if (connection.State != ConnectionState.Active)
            return ConnectionToolSet.None;

        return new ConnectionToolSet(SqlStructureBrowser: ProviderFor<ISqlStructureBrowser>(connection) is not null);
    }

    /// <summary>
    /// Both grants, on the Connection itself, or nothing. Evaluated BEFORE any provider lookup, and with one message for
    /// every refusal, so the outcome never depends on (and so never reveals) whether the connector has a tool.
    /// </summary>
    internal async ValueTask AuthorizeAsync(IActor actor, Connection connection, CancellationToken cancellationToken)
    {
        var identity = connection.GetIdentity();
        foreach (var action in new[] { ActionConstants.ConnectionRead, ActionConstants.ConnectionBrowse })
        {
            var allowed = await _authorizationEvaluation.EvaluateAsync(
                new AuthorizationEvaluationContext(
                    actor,
                    new PermissionDescriptor(action, GovernedResourceDescriptor.FromIdentity(identity)),
                    GovernedResourceDescriptor.FromIdentity(identity),
                    identity),
                cancellationToken).ConfigureAwait(false);
            if (!allowed)
                throw new UnauthorizedAccessException("Not authorized to use tools for this connection.");
        }
    }

    /// <summary>The provider for this Connection's connector, if one is registered here, available, and of the wanted typed kind.</summary>
    internal T? ProviderFor<T>(Connection connection) where T : class, IConnectionToolProvider =>
        _providers
            .Where(p => p.ConnectorDefinitionId == connection.ConnectorDefinitionId
                        && p.Availability == ConnectionToolAvailability.Available)
            .OfType<T>()
            .FirstOrDefault();
}
