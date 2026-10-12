using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Application.Services;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// The single authorized entry point to a Connection's tools. Answering "which tools?" is local-only: it depends on the
/// Connection repository, authorization and the providers connector packages registered, and on no client factory,
/// connection resolver or secret resolver, so it cannot create a remote client, resolve a secret or touch the network.
/// Running a tool is the only thing here that reaches a remote system, and only after BOTH grants are held on the Connection
/// and only for an Active Connection with a provider, on an operator-invoked request. Each such request is audited once,
/// and the audit record is written BEFORE the result is returned: no audit, no data (a required security event, write class W2,
/// committed on its own before the result is delivered; AUD-1.4 slice 5). A request that ended in an exception or a cancellation
/// has no result to withhold: it is recorded best-effort as a separate attempt.
/// </summary>
public sealed class ConnectionToolAccess : IConnectionToolAccess
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IReadOnlyList<IConnectionToolProvider> _providers;
    private readonly IAuditEventWriter _auditEvents;
    private readonly ConnectionToolRequestLimiter _limiter;

    public ConnectionToolAccess(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IConnectionRepository connectionRepository,
        IEnumerable<IConnectionToolProvider> providers,
        IAuditEventWriter auditEvents,
        ConnectionToolRequestLimiter limiter)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _connectionRepository = connectionRepository;
        _providers = providers.ToList();
        _auditEvents = auditEvents;
        _limiter = limiter;
    }

    /// <inheritdoc />
    public async ValueTask<ConnectionToolSet?> GetToolsAsync(ConnectionId id, CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);

        var connection = await _connectionRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (connection is null)
            return null;

        await AuthorizeAsync(actor, connection, cancellationToken).ConfigureAwait(false);

        // A retired Connection is excluded from binding eligibility and must not drive live operations.
        if (connection.State != ConnectionState.Active)
            return ConnectionToolSet.None;

        return new ConnectionToolSet(SqlStructureBrowser: ProviderFor<ISqlStructureBrowser>(connection) is not null);
    }

    /// <inheritdoc />
    public ValueTask<SqlStructureResult<SqlSchemaInfo>?> ListSqlSchemasAsync(ConnectionId id, CancellationToken cancellationToken = default) =>
        BrowseAsync(id, "schemas", [], (browser, connection) => browser.ListSchemasAsync(connection, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public ValueTask<SqlStructureResult<SqlTableInfo>?> ListSqlTablesAsync(ConnectionId id, string schema, CancellationToken cancellationToken = default) =>
        BrowseAsync(id, "tables", [(schema, nameof(schema))], (browser, connection) => browser.ListTablesAsync(connection, schema, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public ValueTask<SqlStructureResult<SqlColumnInfo>?> ListSqlColumnsAsync(ConnectionId id, string schema, string table, CancellationToken cancellationToken = default) =>
        BrowseAsync(id, "columns", [(schema, nameof(schema)), (table, nameof(table))], (browser, connection) => browser.ListColumnsAsync(connection, schema, table, cancellationToken), cancellationToken);

    /// <summary>
    /// Order matters and is the same for every operation: actor, then the Connection is loaded FROM THE STORE by id (a
    /// client-supplied Connection is never trusted), then both grants, then state and provider, then the remote request, then
    /// the audit record, and only then the result. A caller without both grants never reaches the provider.
    /// </summary>
    private async ValueTask<SqlStructureResult<T>?> BrowseAsync<T>(
        ConnectionId id,
        string operation,
        (string? Value, string Name)[] names,
        Func<ISqlStructureBrowser, Connection, ValueTask<SqlStructureResult<T>>> run,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);

        var connection = await _connectionRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (connection is null)
            return null;

        await AuthorizeAsync(actor, connection, cancellationToken).ConfigureAwait(false);

        if (connection.State != ConnectionState.Active)
            throw new ConnectionToolNotAvailableException();
        var browser = ProviderFor<ISqlStructureBrowser>(connection) ?? throw new ConnectionToolNotAvailableException();

        // Names are validated HERE, after authorization and before anything remote, with fixed messages. A bad request never
        // reached the remote system, so it is not an audited remote operation.
        foreach (var (value, name) in names)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ConnectionToolRequestException("The " + name + " name is required.", name);
            if (value.Length > SqlStructureLimits.MaxIdentifierLength)
                throw new ConnectionToolRequestException($"The {name} name is at most {SqlStructureLimits.MaxIdentifierLength} characters.", name);
        }

        // Bounded concurrency per Connection: refused immediately, before any client exists or any secret is resolved.
        using var slot = _limiter.TryEnter(connection.Id.Value) ?? throw new ConnectionToolBusyException();

        SqlStructureResult<T> result;
        try
        {
            result = await run(browser, connection).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The remote request was started and abandoned by the caller: still an attempt worth recording.
            await TryRecordFailureAsync(connection, operation, cancelled: true).ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            await TryRecordFailureAsync(connection, operation, cancelled: false).ConfigureAwait(false);
            throw;
        }

        // Not best-effort: if the record cannot be written the caller does not get the data (the exception propagates and
        // the result is dropped). Written with no caller token so a request cancelled at the last moment is still recorded. The
        // row records the operation and how it ended, with counts; never the names or content the remote system returned.
        await _auditEvents.WriteAsync(
            AuditEvents.ConnectionStructureBrowsed(
                connection.Id.Value,
                connection.Name,
                connection.ConnectorDefinitionId.Value,
                operation,
                result.Outcome,
                result.Outcome == ConnectionToolOutcome.Succeeded ? result.Items.Count : null,
                result.Outcome == ConnectionToolOutcome.Succeeded ? result.Truncated : null,
                connection.ResourceContextId.Value),
            AuditActor.CurrentPrincipal,
            CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    private async ValueTask<IActor> RequireActorAsync(CancellationToken cancellationToken) =>
        await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No authenticated actor.");

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

    /// <summary>
    /// Failure paths only: the request is already failing, and a broken audit sink must not replace the real error. A best-effort
    /// attempt row of the same principal (write class W3); a failure to write it is logged by the writer and changes nothing.
    /// </summary>
    private async ValueTask TryRecordFailureAsync(Connection connection, string operation, bool cancelled)
    {
        try
        {
            await _auditEvents.TryWriteAsync(
                AuditEvents.ConnectionStructureBrowseFailed(
                    connection.Id.Value, connection.Name, connection.ConnectorDefinitionId.Value, operation, cancelled, connection.ResourceContextId.Value),
                AuditActor.CurrentPrincipal,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Deliberately swallowed: see summary.
        }
    }
}
