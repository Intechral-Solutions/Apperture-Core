using System.Linq;
using System.Runtime.ExceptionServices;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
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
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

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
        IAuditedUnitOfWork? auditedUnitOfWork = null)
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
        _auditedUnitOfWork = auditedUnitOfWork;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new AuditInvariantException("Connection changes require the audited unit of work.");

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
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 5). Everything that changes state happens inside it, in this order: the
    /// ownership gate (a new Active connection is an archive-blocking resource entering a context), the owning context (a supplied
    /// context must exist and be Active; none means the reserved Shared context), the operator-supplied secret material, the
    /// connection material check, then <c>connection.created</c> with one <c>connection.secret.stored</c> row per stored field, and
    /// the connection. The secret store joins the unit's transaction, so a refusal, a failed save or a failed audit insert rolls the
    /// stored secrets back with the connection: no secret is left behind, and none is live unless the unit commits.</para>
    /// <para>The identity is chosen before the unit, so a rerun after a confirmed rollback creates the same connection and a
    /// verified commit after a lost acknowledgement is reconstructed from the committed row by that id.</para>
    /// </remarks>
    public async ValueTask<Connection> CreateConnectionAsync(CreateConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var unit = Unit;
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

        foreach (var kv in normalizedSecretRefs)
            ValidateIncomingSecretRef(def, kv.Key, kv.Value);
        ApplyCampusNexusAuthModeDefault(def, normalizedConfig);

        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ConnectionCreated, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                // Gate first: a new Active Connection is an archive-blocking resource entering a context, so this must not
                // interleave with a concurrent Archive evaluating that same context's ownership summary (CTX-3B follow-on).
                await _ownershipGate.AcquireAsync(ct).ConfigureAwait(false);

                // Ownership defaults to the reserved Shared ResourceContext when the caller supplies none, so existing create
                // clients stay compatible. A supplied context must exist and be Active.
                var owningContext = await _resourceContexts
                    .ResolveForNewOwnershipAsync(request.ResourceContextId, ct)
                    .ConfigureAwait(false);

                // The identity is minted here, so no secret can already be owned by this connection: any supplied reference
                // names someone else's secret (or none) and is refused. Secret material comes in through SecretValues.
                await _secretProvisioner.EnsureReferencesOwnedAsync(id, normalizedSecretRefs, ct).ConfigureAwait(false);

                // Rebuilt on every attempt: a rerun after a confirmed rollback starts from the request again.
                var mergedSecretRefs = new Dictionary<string, string>(normalizedSecretRefs, StringComparer.Ordinal);
                var storedFields = new List<string>();
                if (normalizedSecretValues is { Count: > 0 })
                {
                    // The store joins this unit's transaction, so the secrets exist and are live only if the unit commits.
                    var provisioned = await _secretProvisioner
                        .ProvisionForCreateAsync(id, def, normalizedSecretValues, ct)
                        .ConfigureAwait(false);
                    foreach (var kv in provisioned.OrderBy(k => k.Key, StringComparer.Ordinal))
                    {
                        mergedSecretRefs[kv.Key] = kv.Value;
                        storedFields.Add(kv.Key);
                    }
                }

                ConnectionSecretMaterialProvisioner.ValidateConnectionMaterial(def, normalizedConfig, mergedSecretRefs);

                var connection = new Connection(
                    id,
                    request.ConnectorDefinitionId,
                    request.Name,
                    new Dictionary<string, string>(normalizedConfig, StringComparer.Ordinal),
                    mergedSecretRefs,
                    request.ScopeKey,
                    request.OwnerId,
                    ConnectionState.Active)
                {
                    ResourceContextId = owningContext.Id
                };

                await transaction.StageAsync(
                    AuditEvents.ConnectionCreated(id.Value, connection.Name, connection.ConnectorDefinitionId.Value, storedFields.Count, owningContext.Id.Value),
                    ct).ConfigureAwait(false);
                foreach (var field in storedFields)
                {
                    await transaction.StageAsync(
                        AuditEvents.ConnectionSecretStored(id.Value, field, replaced: false, owningContext.Id.Value),
                        ct).ConfigureAwait(false);
                }

                await _connectionRepository.AddAsync(connection, ct).ConfigureAwait(false);
                return connection;
            },
            async (committed, ct) => await _connectionRepository.GetByIdAsync(id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Connection {id.Value} could not be read back."),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 5), and the fix of the live-secret-first defect. An early read and
    /// <c>connection.update</c> check answer a missing or forbidden request before a unit opens; they decide nothing. Inside the
    /// attempt, in this order: the connection row, locked to the end of the transaction (the row an ownership move and a retire
    /// also take), and <c>connection.update</c> authorized again against it as locked, so a move that committed while this request
    /// waited is what the decision reads; the change computed against the locked row; the operator-supplied secret material, added
    /// and activated through the store inside this transaction; the material check; the primary row and one
    /// <c>connection.secret.stored</c> row per field whose material was stored; then the row. The credential a connection resolves
    /// therefore changes only if the unit commits: a refusal, a failed save, a failed audit insert or an unknown outcome that was
    /// not committed leaves the previous credential live, with nothing to compensate.</para>
    /// <para>No ownership gate: an update can neither make a connection live nor move it, and a move takes the gate and then
    /// this row, so an update waits behind a move and re-authorizes against the new owner. The merge is made over the locked row,
    /// so two concurrent updates of different fields both survive. A request that changes nothing declares a no-op and records
    /// nothing. The row is <c>connection.secret_rotated</c> when any secret field changed (material rotated, or a reference added,
    /// removed or pointed elsewhere), otherwise <c>connection.updated</c>.</para>
    /// <para><b>Secret fields (SEC-2.2).</b> A request cannot name a reference. It can give a declared field a value
    /// (<c>SecretValues</c>), which rotates the secret the field owns or, when the field names nothing it owns, stores a new
    /// secret and repoints the field in this same unit; and it can remove any stored reference, an undeclared legacy key
    /// included (<c>ClearSecretFields</c>). Neither reads, versions, disables or adopts the secret a removed or replaced reference
    /// named, so a poisoned field is repaired without knowing the reference and without touching its target.</para>
    /// </remarks>
    public async ValueTask<Connection> UpdateConnectionAsync(ConnectionId id, UpdateConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var unit = Unit;
        var early = await _connectionRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Connection {id.Value} not found.");
        await AuthorizeAsync(actor, early, ActionConstants.ConnectionUpdate, "Not authorized to update this connection.", cancellationToken).ConfigureAwait(false);

        var def = _connectorRegistry.Get(early.ConnectorDefinitionId)
            ?? throw new InvalidOperationException($"Connector definition {early.ConnectorDefinitionId.Value} not found.");

        // Request-only preparation: nothing here depends on the stored connection.
        Dictionary<string, string>? requestedConfig = request.Config is null
            ? null
            : ConnectionSecretMaterialProvisioner.NormalizeToDeclaredFieldKeys(request.Config, def);

        IReadOnlyDictionary<string, string>? secretValuesToApply = null;
        if (request.SecretValues is { Count: > 0 })
            secretValuesToApply = ConnectionSecretMaterialProvisioner.NormalizeToDeclaredFieldKeys(request.SecretValues, def);

        var clearRequested = NormalizeClearRequest(request.ClearSecretFields);
        foreach (var requested in clearRequested)
        {
            if (secretValuesToApply is not null
                && secretValuesToApply.Any(kv => !string.IsNullOrWhiteSpace(kv.Value) && string.Equals(kv.Key, requested, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidConnectionRequestException($"Secret field '{requested}' cannot be cleared and given a value in the same request.");
            }
        }

        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ConnectionUpdated, AuditActor.CurrentPrincipal, AuditActionNames.ConnectionUpdateActions),
            async (transaction, ct) =>
            {
                var fresh = await _connectionRepository.GetByIdForUpdateAsync(id, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Connection {id.Value} not found.");
                await AuthorizeAsync(actor, fresh, ActionConstants.ConnectionUpdate, "Not authorized to update this connection.", ct).ConfigureAwait(false);

                var nextConfig = requestedConfig is null
                    ? new Dictionary<string, string>(fresh.Config, StringComparer.Ordinal)
                    : new Dictionary<string, string>(requestedConfig, StringComparer.Ordinal);

                // The references as stored, every key included: a key the connector no longer declares is kept (and can be
                // cleared) rather than refused or dropped. They are never normalized or validated here, because a stored
                // reference of any shape is a legacy row that resolution refuses and the two field operations repair.
                var workingSecretRefs = new Dictionary<string, string>(fresh.SecretRefs, StringComparer.Ordinal);
                foreach (var requested in clearRequested)
                {
                    if (ResolveStoredSecretKey(def, workingSecretRefs.Keys, requested) is { } stored)
                        workingSecretRefs.Remove(stored);
                }

                // Mirrors the provisioner, which skips blank values: only fields that actually receive material count.
                var materialFields = secretValuesToApply is null
                    ? new List<string>()
                    : secretValuesToApply.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key)
                        .OrderBy(k => k, StringComparer.Ordinal).ToList();

                ApplyCampusNexusAuthModeDefault(def, nextConfig);
                var nameChanged = request.Name is not null && !string.Equals(request.Name, fresh.Name, StringComparison.Ordinal);

                // The connector's defaults are applied to the stored config too before comparing: a legacy row that never stored
                // the default must not make a request that changes nothing look like a change to it.
                var baselineConfig = new Dictionary<string, string>(fresh.Config, StringComparer.Ordinal);
                ApplyCampusNexusAuthModeDefault(def, baselineConfig);
                var configChanged = ChangedKeys(baselineConfig, nextConfig);
                var refsChanged = ChangedKeys(fresh.SecretRefs, workingSecretRefs);

                // Config decides where a credential is sent (a host, a base URL), so when the request changes config every
                // reference the connection keeps must be its own: one that is not must not travel with a new destination. A
                // field this request gives a new value is about to be replaced, and a cleared one is gone, so neither is asked.
                // A request that only renames, or leaves config as stored, does not re-prove a legacy reference (such a row
                // stays editable and can be repaired by the two field operations); resolution refuses it either way.
                if (configChanged.Count > 0)
                {
                    await _secretProvisioner.EnsureReferencesOwnedAsync(
                        fresh.Id,
                        workingSecretRefs.Where(kv => !materialFields.Contains(kv.Key, StringComparer.Ordinal)),
                        ct).ConfigureAwait(false);
                }

                if (!nameChanged && configChanged.Count == 0 && refsChanged.Count == 0 && materialFields.Count == 0)
                {
                    transaction.DeclareNoOp();
                    return fresh;
                }

                // The material goes to the store inside this transaction, after the row is locked and the request is known to
                // change something. It becomes the connection's live credential only if the unit commits. A field whose
                // reference is not its own gets a new secret and is repointed to it here, together with everything else.
                IReadOnlyList<ConnectionSecretWrite> writes = [];
                if (secretValuesToApply is not null && materialFields.Count > 0)
                {
                    writes = await _secretProvisioner
                        .ApplySecretValuesForUpdateAsync(fresh.Id, def, secretValuesToApply, workingSecretRefs, ct)
                        .ConfigureAwait(false);
                }

                ConnectionSecretMaterialProvisioner.ValidateUpdatedConnectionMaterial(def, nextConfig, workingSecretRefs);

                // A field also counts as rotated when its secret reference was added, removed or now points somewhere new
                // (a count only; see AuditEvents.ConnectionChanged).
                var rotated = new SortedSet<string>(materialFields, StringComparer.Ordinal);
                foreach (var key in ChangedKeys(fresh.SecretRefs, workingSecretRefs))
                    rotated.Add(key);

                var updated = fresh with
                {
                    Name = request.Name ?? fresh.Name,
                    Config = nextConfig,
                    SecretRefs = workingSecretRefs
                };

                var contextId = fresh.ResourceContextId.Value;
                await transaction.StageAsync(
                    AuditEvents.ConnectionChanged(
                        id.Value, updated.Name, updated.ConnectorDefinitionId.Value, nameChanged, configChanged, rotated.ToList(), contextId),
                    ct).ConfigureAwait(false);
                foreach (var write in writes.OrderBy(w => w.FieldKey, StringComparer.Ordinal))
                {
                    await transaction.StageAsync(
                        AuditEvents.ConnectionSecretStored(id.Value, write.FieldKey, write.Replaced, contextId),
                        ct).ConfigureAwait(false);
                }

                await _connectionRepository.UpdateAsync(updated, ct).ConfigureAwait(false);
                return updated;
            },
            async (committed, ct) => await _connectionRepository.GetByIdAsync(id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Connection {id.Value} not found."),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 5). Inside the attempt: the connection row, locked, <c>connection.delete</c>
    /// authorized again against it as locked, then the state change and <c>connection.retired</c>. Retiring a retired connection
    /// changes nothing and records nothing. No ownership gate: retiring only removes an archive blocker and moves nothing, and
    /// waiting behind a move on the row is enough. Retiring does not touch the connection's secrets or its bindings.</para>
    /// </remarks>
    public async ValueTask RetireConnectionAsync(ConnectionId id, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var unit = Unit;
        var early = await _connectionRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Connection {id.Value} not found.");
        await AuthorizeAsync(actor, early, ActionConstants.ConnectionDelete, "Not authorized to retire this connection.", cancellationToken).ConfigureAwait(false);

        await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ConnectionRetired, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var fresh = await _connectionRepository.GetByIdForUpdateAsync(id, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Connection {id.Value} not found.");
                await AuthorizeAsync(actor, fresh, ActionConstants.ConnectionDelete, "Not authorized to retire this connection.", ct).ConfigureAwait(false);

                if (fresh.State == ConnectionState.Retired)
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                var retired = fresh with { State = ConnectionState.Retired };
                await transaction.StageAsync(
                    AuditEvents.ConnectionRetired(id.Value, retired.Name, retired.ConnectorDefinitionId.Value, fresh.ResourceContextId.Value),
                    ct).ConfigureAwait(false);
                await _connectionRepository.UpdateAsync(retired, ct).ConfigureAwait(false);
                return true;
            },
            // A verified commit is the retirement having happened; the caller returns nothing.
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask AuthorizeAsync(IActor actor, Connection connection, string action, string refusal, CancellationToken cancellationToken)
    {
        var identity = connection.GetIdentity();
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(action, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity),
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException(refusal);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One external attempt and one audited unit</b> (AUD-1.4 slice 5; design gate §11.6, §13.3). Test-connect uses the
    /// credentials the connection stores, so it needs <c>connection.verify</c>, a permission of its own, <b>in addition to</b>
    /// <c>connection.read</c> (as structure browse does): neither alone authorizes it. It only updates verification metadata. An
    /// early read and both checks authorize the probe. The probe itself runs first, outside any transaction: a transaction never
    /// makes an external effect atomic, and the stored material is used against the remote host whatever happens next.</para>
    /// <para>One unit then records what happened. It locks the connection row, writes <b>only</b> the three verification columns
    /// over the row as locked (so an edit, rotation or retirement made while the probe ran is kept, not overwritten with the
    /// snapshot the probe started from), and stages <c>connection.verified</c> (basis <c>external_attempt</c>). The row states
    /// what the verifier answered and nothing more: <c>succeeded</c> only for a success, <c>failed</c> for a failure answer or a
    /// verifier that threw (no status), <c>unknown</c> when it answered that verification was unavailable. The verifier's summary
    /// is remote-derived text and stays on the connection row; it is never recorded in the audit trail. A verifier that threw is
    /// recorded and then its exception is rethrown unchanged.</para>
    /// <para>The unit runs with no caller token: the attempt happened, so a request cancelled during the probe still leaves its
    /// evidence. No second authorization is made inside the unit: the probe was authorized before it ran and cannot be undone, so
    /// what it did is recorded, and the unit changes only verification metadata. If the unit itself cannot commit, the attempt is
    /// unrecorded and the caller is told so.</para>
    /// </remarks>
    public async ValueTask<Connection> VerifyConnectionAsync(ConnectionId id, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var unit = Unit;
        var existing = await _connectionRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Connection {id.Value} not found.");
        // Both are required, and both are checked before the probe: reading a connection does not reach the credentials it stores, and
        // verifying one without being able to read it would return its detail to a caller who could not have asked for it.
        await AuthorizeAsync(actor, existing, ActionConstants.ConnectionRead, "Not authorized to verify this connection.", cancellationToken).ConfigureAwait(false);
        await AuthorizeAsync(actor, existing, ActionConstants.ConnectionVerify, "Not authorized to verify this connection.", cancellationToken).ConfigureAwait(false);

        ConnectionVerificationResult? verification = null;
        ExceptionDispatchInfo? failure = null;
        try
        {
            verification = await _connectionVerification.VerifyAsync(existing, cancellationToken);
        }
        catch (Exception ex)
        {
            failure = ExceptionDispatchInfo.Capture(ex);
        }

        var recorded = await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ConnectionVerified, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var fresh = await _connectionRepository.GetByIdForUpdateAsync(id, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Connection {id.Value} not found.");

                // A verifier is meant to answer success, failure or unavailable; a "never tested" answer is no answer.
                ConnectionVerificationStatus? status = verification is null
                    ? null
                    : verification.Status == ConnectionVerificationStatus.NeverTested ? ConnectionVerificationStatus.Unavailable : verification.Status;
                await transaction.StageAsync(
                    AuditEvents.ConnectionVerified(
                        id.Value, fresh.Name, fresh.ConnectorDefinitionId.Value, status, fresh.ResourceContextId.Value,
                        noAnswerBecauseCancelled: failure?.SourceException is OperationCanceledException),
                    ct).ConfigureAwait(false);
                if (verification is null)
                    return fresh;

                var updated = fresh with
                {
                    LastVerificationStatus = verification.Status,
                    LastVerificationAtUtc = verification.VerifiedAtUtc,
                    LastVerificationSummary = verification.Summary
                };
                await _connectionRepository.UpdateAsync(updated, ct).ConfigureAwait(false);
                return updated;
            },
            async (committed, ct) => await _connectionRepository.GetByIdAsync(id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Connection {id.Value} not found."),
            CancellationToken.None).ConfigureAwait(false);

        failure?.Throw();
        return recorded;
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

    /// <summary>The most keys one request may clear. A connection has a handful of secret fields; the bound only stops an absurd request.</summary>
    private const int MaxClearedFields = 64;

    /// <summary>The longest key a request may name. Stored keys are bounded by the column, and a key is never echoed back longer than this.</summary>
    private const int MaxFieldKeyLength = 256;

    /// <summary>Trims and de-duplicates the keys to clear. A blank or oversized key is a request the caller can fix, so it is a 400.</summary>
    private static List<string> NormalizeClearRequest(IReadOnlyList<string>? requested)
    {
        var keys = new List<string>();
        if (requested is null)
            return keys;
        if (requested.Count > MaxClearedFields)
            throw new InvalidConnectionRequestException($"At most {MaxClearedFields} secret fields can be cleared in one request.");
        foreach (var raw in requested)
        {
            var key = raw?.Trim();
            if (string.IsNullOrEmpty(key))
                throw new InvalidConnectionRequestException("A secret field to clear needs a key.");
            if (key.Length > MaxFieldKeyLength)
                throw new InvalidConnectionRequestException($"A secret field key is at most {MaxFieldKeyLength} characters.");
            if (!keys.Contains(key, StringComparer.Ordinal))
                keys.Add(key);
        }

        return keys;
    }

    /// <summary>
    /// The stored key a clear request names, or null when it names a declared secret field that holds no reference (clearing
    /// nothing is not an error, so a retried request is the same request). The key is matched exactly first, so an undeclared
    /// legacy key is cleared by its own spelling; then against the connector's declared secret fields, case-insensitively, as
    /// every other field key is; then against a single stored key that differs only in case.
    /// </summary>
    /// <exception cref="InvalidConnectionRequestException">The key is neither a secret field of the connector nor a stored key.</exception>
    private static string? ResolveStoredSecretKey(ConnectorDefinition def, IEnumerable<string> storedKeys, string requested)
    {
        var stored = storedKeys.ToList();
        if (stored.Contains(requested, StringComparer.Ordinal))
            return requested;

        var declared = def.ConnectionFields.FirstOrDefault(f => string.Equals(f.Key, requested, StringComparison.OrdinalIgnoreCase));
        if (declared is not null)
        {
            if (!declared.IsSecret)
                throw new InvalidConnectionRequestException($"'{requested}' is not a secret field.");
            return stored.Contains(declared.Key, StringComparer.Ordinal) ? declared.Key : null;
        }

        var sameIgnoringCase = stored.Where(k => string.Equals(k, requested, StringComparison.OrdinalIgnoreCase)).ToList();
        if (sameIgnoringCase.Count == 1)
            return sameIgnoringCase[0];

        throw new InvalidConnectionRequestException($"'{requested}' is not a secret field of this connection.");
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
