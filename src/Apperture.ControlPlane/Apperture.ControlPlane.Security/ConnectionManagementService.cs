using System.Linq;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Connection management workflow with authorization enforcement.
/// </summary>
public sealed class ConnectionManagementService : IConnectionManagementService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IConnectorDefinitionRegistry _connectorRegistry;
    private readonly IConnectionVerificationService _connectionVerification;
    private readonly ConnectionSecretMaterialProvisioner _secretProvisioner;
    private readonly IResourceContextService _resourceContexts;
    private readonly AppertureDbContext _db;
    private readonly IResourceOwnershipMutationGate _ownershipGate;
    private readonly IAuditService _audit;

    public ConnectionManagementService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IConnectionRepository connectionRepository,
        IConnectorDefinitionRegistry connectorRegistry,
        IConnectionVerificationService connectionVerification,
        ConnectionSecretMaterialProvisioner secretProvisioner,
        IResourceContextService resourceContexts,
        AppertureDbContext db,
        IResourceOwnershipMutationGate ownershipGate,
        IAuditService audit)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _connectionRepository = connectionRepository;
        _connectorRegistry = connectorRegistry;
        _connectionVerification = connectionVerification;
        _secretProvisioner = secretProvisioner;
        _resourceContexts = resourceContexts;
        _db = db;
        _ownershipGate = ownershipGate;
        _audit = audit;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Connection>> ListConnectionsAsync(CancellationToken cancellationToken = default) =>
        ListConnectionsAsync(resourceContextId: null, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Connection>> ListConnectionsAsync(
        ResourceContextId? resourceContextId,
        CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ConnectionRead, new ResourceDescriptor(ResourceTypeConstants.Connection, ScopeKeyConstants.Instance)),
                null),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to list connections.");

        var scopeKey = ScopeKeyConstants.Instance;
        return await _connectionRepository.GetVisibleAsync(scopeKey, resourceContextId, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Connection>> ListConnectionsByConnectorAsync(
        ConnectorDefinitionId connectorDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        // Same gate as the ordinary list: connection.read at instance scope. There is no cheaper path around it.
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ConnectionRead, new ResourceDescriptor(ResourceTypeConstants.Connection, ScopeKeyConstants.Instance)),
                null),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to list connections.");

        return await _connectionRepository.GetVisibleByConnectorDefinitionAsync(
            ScopeKeyConstants.Instance, connectorDefinitionId, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Connection?> GetConnectionAsync(ConnectionId id, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var connection = await _connectionRepository.GetByIdAsync(id, cancellationToken);
        if (connection is null)
            return null;

        var identity = connection.GetIdentity();
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ConnectionRead, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to view this connection.");

        return connection;
    }

    /// <inheritdoc />
    public async ValueTask<Connection> CreateConnectionAsync(CreateConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ConnectionCreate, new ResourceDescriptor(ResourceTypeConstants.Connection, request.ScopeKey ?? ScopeKeyConstants.Instance)),
                null),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to create connections.");

        var def = _connectorRegistry.Get(request.ConnectorDefinitionId);
        if (def is null)
            throw new InvalidOperationException($"Connector definition {request.ConnectorDefinitionId.Value} not found.");

        var id = new ConnectionId(Guid.NewGuid().ToString("N")[..24]);

        var normalizedConfig = ConnectionSecretMaterialProvisioner.NormalizeToDeclaredFieldKeys(request.Config, def);
        var normalizedSecretRefs = ConnectionSecretMaterialProvisioner.NormalizeToDeclaredFieldKeys(request.SecretRefs, def);
        IReadOnlyDictionary<string, string>? normalizedSecretValues = null;
        if (request.SecretValues is { Count: > 0 })
            normalizedSecretValues = ConnectionSecretMaterialProvisioner.NormalizeToDeclaredFieldKeys(request.SecretValues, def);

        var mergedSecretRefs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in normalizedSecretRefs)
        {
            ValidateIncomingSecretRef(def, kv.Key, kv.Value);
            mergedSecretRefs[kv.Key] = kv.Value;
        }

        if (normalizedSecretValues is { Count: > 0 })
        {
            // Advisory, non-authoritative pre-check (CTX-3B review finding I2). ProvisionForCreateAsync
            // writes secret material to an external store and cannot participate in the SQL
            // transaction below, so without this check an archived/nonexistent target context would
            // let this write happen and then throw once the REAL check below runs -- orphaning that
            // secret material, since the Connection row it would belong to is never created. This call
            // is purely to avoid that common-case wasted external write on the failure path; it is NOT
            // the correctness mechanism (it runs outside the ownership gate and can theoretically race
            // a concurrent Archive). The authoritative, race-safe check is the
            // ResolveForNewOwnershipAsync call inside the gated transaction further below, which alone
            // is what prevents the create-vs-archive race.
            await _resourceContexts.ResolveForNewOwnershipAsync(request.ResourceContextId, cancellationToken).ConfigureAwait(false);

            var provisioned = await _secretProvisioner
                .ProvisionForCreateAsync(id, def, normalizedSecretValues, cancellationToken)
                .ConfigureAwait(false);
            foreach (var kv in provisioned)
                mergedSecretRefs[kv.Key] = kv.Value;
        }

        ApplyCampusNexusAuthModeDefault(def, normalizedConfig);
        ConnectionSecretMaterialProvisioner.ValidateConnectionMaterial(def, normalizedConfig, mergedSecretRefs);

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            // Gate first: a new Active Connection is an archive-blocking resource entering a context, so
            // this must not interleave with a concurrent Archive evaluating that same context's ownership
            // summary (CTX-3B follow-on: closes the create-vs-archive race Plan 2's own final review flagged).
            await _ownershipGate.AcquireAsync(cancellationToken).ConfigureAwait(false);

            // Ownership defaults to the reserved Shared ResourceContext when the caller supplies none, so
            // existing create clients stay compatible. A supplied context must exist and be Active.
            // Ownership never affects secret provisioning above: secret refs are opaque and stay put.
            var owningContext = await _resourceContexts
                .ResolveForNewOwnershipAsync(request.ResourceContextId, cancellationToken)
                .ConfigureAwait(false);

            var connection = new Connection(
                id,
                request.ConnectorDefinitionId,
                request.Name,
                normalizedConfig,
                mergedSecretRefs,
                request.ScopeKey,
                request.OwnerId,
                ConnectionState.Active)
            {
                ResourceContextId = owningContext.Id
            };

            await _connectionRepository.AddAsync(connection, cancellationToken).ConfigureAwait(false);

            // Inside the transaction: the Connection and its creation record commit (or roll back) together.
            await AppendAuditAsync(
                actor, ConnectionAuditActionNames.Created, "succeeded", connection,
                new ConnectionAuditDetails(
                    ResourceMoveResourceTypes.Connection, connection.Id.Value, connection.Name, connection.ConnectorDefinitionId.Value),
                cancellationToken).ConfigureAwait(false);

            if (tx is not null)
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<Connection> UpdateConnectionAsync(ConnectionId id, UpdateConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var existing = await _connectionRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Connection {id.Value} not found.");

        var identity = existing.GetIdentity();
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ConnectionUpdate, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to update this connection.");

        var def = _connectorRegistry.Get(existing.ConnectorDefinitionId)
            ?? throw new InvalidOperationException($"Connector definition {existing.ConnectorDefinitionId.Value} not found.");

        var nextConfigDict = request.Config is null
            ? new Dictionary<string, string>(existing.Config, StringComparer.Ordinal)
            : ConnectionSecretMaterialProvisioner.NormalizeToDeclaredFieldKeys(request.Config, def);

        var workingSecretRefs = new Dictionary<string, string>(StringComparer.Ordinal);
        var secretRefsSource = request.SecretRefs ?? existing.SecretRefs;
        foreach (var kv in ConnectionSecretMaterialProvisioner.NormalizeToDeclaredFieldKeys(secretRefsSource, def))
            workingSecretRefs[kv.Key] = kv.Value;
        foreach (var kv in workingSecretRefs)
            ValidateIncomingSecretRef(def, kv.Key, kv.Value);

        var rotatedSecretFields = new SortedSet<string>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, string>? secretValuesToApply = null;
        if (request.SecretValues is { Count: > 0 })
        {
            secretValuesToApply = ConnectionSecretMaterialProvisioner.NormalizeToDeclaredFieldKeys(request.SecretValues, def);
            // Mirrors the provisioner, which skips blank values: only fields that actually receive material count.
            foreach (var kv in secretValuesToApply.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)))
                rotatedSecretFields.Add(kv.Key);
        }

        // Secret material is written to the store BEFORE the row is validated and saved, and the store cannot join
        // the row's transaction. So once material may have been written, a later failure must still leave a record
        // that the rotation was attempted (outcome "failed"), rather than a silent change to a credential.
        var secretMaterialMayHaveChanged = rotatedSecretFields.Count > 0;

        Connection updated;
        try
        {
            if (secretValuesToApply is not null)
            {
                await _secretProvisioner
                    .ApplySecretValuesForUpdateAsync(existing.Id, def, secretValuesToApply, workingSecretRefs, cancellationToken)
                    .ConfigureAwait(false);
            }

            ApplyCampusNexusAuthModeDefault(def, nextConfigDict);
            ConnectionSecretMaterialProvisioner.ValidateConnectionMaterial(def, nextConfigDict, workingSecretRefs);

            updated = existing with
            {
                Name = request.Name ?? existing.Name,
                Config = nextConfigDict,
                SecretRefs = workingSecretRefs
            };

            // A field also counts as rotated when its secret reference was added, removed or now points somewhere new
            // (key names only).
            foreach (var key in existing.SecretRefs.Keys.Union(workingSecretRefs.Keys, StringComparer.Ordinal))
            {
                existing.SecretRefs.TryGetValue(key, out var previousRef);
                workingSecretRefs.TryGetValue(key, out var nextRef);
                if (!string.Equals(previousRef, nextRef, StringComparison.Ordinal))
                    rotatedSecretFields.Add(key);
            }

            await _connectionRepository.UpdateAsync(updated, cancellationToken);
        }
        catch when (secretMaterialMayHaveChanged)
        {
            await TryAppendAuditAsync(
                actor, ConnectionAuditActionNames.SecretRotated, "failed", existing,
                new ConnectionAuditDetails(
                    ResourceMoveResourceTypes.Connection, existing.Id.Value, existing.Name, existing.ConnectorDefinitionId.Value,
                    SecretFieldsRotated: rotatedSecretFields.ToList())).ConfigureAwait(false);
            throw;
        }

        // Exactly one record per update. When any secret field changed it is the secret-rotation record; the
        // details still say whether the name or other fields changed too. Appended after the update has been
        // persisted, matching the other management services' post-commit audit.
        await AppendAuditAsync(
            actor,
            rotatedSecretFields.Count > 0 ? ConnectionAuditActionNames.SecretRotated : ConnectionAuditActionNames.Updated,
            "succeeded",
            updated,
            new ConnectionAuditDetails(
                ResourceMoveResourceTypes.Connection, updated.Id.Value, updated.Name, updated.ConnectorDefinitionId.Value,
                NameChanged: !string.Equals(existing.Name, updated.Name, StringComparison.Ordinal),
                ConfigFieldsChanged: ChangedKeys(existing.Config, updated.Config),
                SecretFieldsRotated: rotatedSecretFields.ToList()),
            cancellationToken).ConfigureAwait(false);
        return updated;
    }

    /// <inheritdoc />
    public async ValueTask RetireConnectionAsync(ConnectionId id, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var existing = await _connectionRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Connection {id.Value} not found.");

        var identity = existing.GetIdentity();
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ConnectionDelete, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to retire this connection.");

        var retired = existing with { State = ConnectionState.Retired };
        await _connectionRepository.UpdateAsync(retired, cancellationToken);

        await AppendAuditAsync(
            actor, ConnectionAuditActionNames.Retired, "succeeded", retired,
            new ConnectionAuditDetails(
                ResourceMoveResourceTypes.Connection, retired.Id.Value, retired.Name, retired.ConnectorDefinitionId.Value),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<Connection> VerifyConnectionAsync(ConnectionId id, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var existing = await _connectionRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Connection {id.Value} not found.");

        var identity = existing.GetIdentity();
        // Diagnostic test-connect is available to anyone who can read the connection; it only updates verification metadata.
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ConnectionRead, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to verify this connection.");

        ConnectionVerificationResult verification;
        try
        {
            verification = await _connectionVerification.VerifyAsync(existing, cancellationToken);
        }
        catch
        {
            // The attempt used stored secrets against a remote host even though it produced no result. Record that it
            // happened; the exception itself (which may echo remote text) is not recorded.
            await TryAppendAuditAsync(
                actor, ConnectionAuditActionNames.Verified, "failed", existing,
                new ConnectionAuditDetails(
                    ResourceMoveResourceTypes.Connection, existing.Id.Value, existing.Name, existing.ConnectorDefinitionId.Value)).ConfigureAwait(false);
            throw;
        }

        var updated = existing with
        {
            LastVerificationStatus = verification.Status,
            LastVerificationAtUtc = verification.VerifiedAtUtc,
            LastVerificationSummary = verification.Summary
        };
        await _connectionRepository.UpdateAsync(updated, cancellationToken);

        // An outbound operation that used stored secrets. Only the status is recorded: the verifier's summary is
        // remote-derived text and stays on the Connection row, never in the audit trail.
        await AppendAuditAsync(
            actor,
            ConnectionAuditActionNames.Verified,
            verification.Status == ConnectionVerificationStatus.Success ? "succeeded" : "failed",
            updated,
            new ConnectionAuditDetails(
                ResourceMoveResourceTypes.Connection, updated.Id.Value, updated.Name, updated.ConnectorDefinitionId.Value,
                VerificationStatus: verification.Status.ToString()),
            cancellationToken).ConfigureAwait(false);
        return updated;
    }

    /// <summary>
    /// Appends one Connection lifecycle audit record. <c>Resource</c> is deliberately <c>null</c>: SqlAuditService appends a
    /// non-null Resource to Details as plain text (corrupting JSON) and never persists it, so Connection identity lives in
    /// the structured Details instead. The owning ResourceContext comes from the Connection the caller already loaded.
    /// </summary>
    private ValueTask AppendAuditAsync(
        IActor actor,
        string action,
        string outcome,
        Connection connection,
        ConnectionAuditDetails details,
        CancellationToken cancellationToken) =>
        _audit.AppendAsync(new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actor.Id,
            action,
            Resource: null,
            Outcome: outcome,
            DateTimeOffset.UtcNow,
            new CorrelationId(Guid.NewGuid().ToString("N")),
            System.Text.Json.JsonSerializer.Serialize(details),
            ResourceContextId: connection.ResourceContextId.Value), cancellationToken);

    /// <summary>
    /// Best-effort append for a failure path that is about to rethrow: a broken audit sink must not replace the
    /// original exception the caller needs to see. Uses no caller token, so a cancelled request still leaves its record.
    /// </summary>
    private async ValueTask TryAppendAuditAsync(
        IActor actor, string action, string outcome, Connection connection, ConnectionAuditDetails details)
    {
        try
        {
            await AppendAuditAsync(actor, action, outcome, connection, details, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Deliberately swallowed: see summary.
        }
    }

    /// <summary>Keys whose value was added, removed or changed. Values are never returned.</summary>
    private static List<string> ChangedKeys(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after) =>
        before.Keys.Union(after.Keys, StringComparer.Ordinal)
            .Where(k => !before.TryGetValue(k, out var b) || !after.TryGetValue(k, out var a) || !string.Equals(a, b, StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Operators often leave <c>AuthMode</c> blank when using an API key; runtime factory defaults to ApiKey semantics when mode is ApiKey.
    /// </summary>
    private static void ApplyCampusNexusAuthModeDefault(ConnectorDefinition def, Dictionary<string, string> config)
    {
        if (!string.Equals(def.Id.Value, WellKnownConnectorDefinitionIds.PlatformCampusNexus.Value, StringComparison.Ordinal))
            return;
        if (!config.TryGetValue("AuthMode", out var mode) || string.IsNullOrWhiteSpace(mode))
            config["AuthMode"] = "ApiKey";
    }

    private static void ValidateIncomingSecretRef(ConnectorDefinition def, string key, string value)
    {
        var field = def.ConnectionFields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
        if (field is null || !field.IsSecret)
            return;
        if (!Guid.TryParse(value, out _))
            throw new InvalidOperationException($"Secret reference for '{key}' must be a secret id (GUID). Use SecretValues to supply new secret material.");
    }
}
