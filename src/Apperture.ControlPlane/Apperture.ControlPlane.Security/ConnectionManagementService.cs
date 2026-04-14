using System.Linq;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

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

    public ConnectionManagementService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IConnectionRepository connectionRepository,
        IConnectorDefinitionRegistry connectorRegistry,
        IConnectionVerificationService connectionVerification,
        ConnectionSecretMaterialProvisioner secretProvisioner)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _connectionRepository = connectionRepository;
        _connectorRegistry = connectorRegistry;
        _connectionVerification = connectionVerification;
        _secretProvisioner = secretProvisioner;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Connection>> ListConnectionsAsync(CancellationToken cancellationToken = default)
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
        return await _connectionRepository.GetVisibleAsync(scopeKey, cancellationToken);
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
            var provisioned = await _secretProvisioner
                .ProvisionForCreateAsync(id, def, normalizedSecretValues, cancellationToken)
                .ConfigureAwait(false);
            foreach (var kv in provisioned)
                mergedSecretRefs[kv.Key] = kv.Value;
        }

        ApplyCampusNexusAuthModeDefault(def, normalizedConfig);
        ConnectionSecretMaterialProvisioner.ValidateConnectionMaterial(def, normalizedConfig, mergedSecretRefs);

        var connection = new Connection(
            id,
            request.ConnectorDefinitionId,
            request.Name,
            normalizedConfig,
            mergedSecretRefs,
            request.ScopeKey,
            request.OwnerId,
            ConnectionState.Active);

        await _connectionRepository.AddAsync(connection, cancellationToken);
        return connection;
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

        if (request.SecretValues is { Count: > 0 })
        {
            var normalizedSecretValues = ConnectionSecretMaterialProvisioner.NormalizeToDeclaredFieldKeys(request.SecretValues, def);
            await _secretProvisioner
                .ApplySecretValuesForUpdateAsync(existing.Id, def, normalizedSecretValues, workingSecretRefs, cancellationToken)
                .ConfigureAwait(false);
        }

        ApplyCampusNexusAuthModeDefault(def, nextConfigDict);
        ConnectionSecretMaterialProvisioner.ValidateConnectionMaterial(def, nextConfigDict, workingSecretRefs);

        var updated = existing with
        {
            Name = request.Name ?? existing.Name,
            Config = nextConfigDict,
            SecretRefs = workingSecretRefs
        };

        await _connectionRepository.UpdateAsync(updated, cancellationToken);
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
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ConnectionUpdate, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to verify this connection.");

        var verification = await _connectionVerification.VerifyAsync(existing, cancellationToken);
        var updated = existing with
        {
            LastVerificationStatus = verification.Status,
            LastVerificationAtUtc = verification.VerifiedAtUtc,
            LastVerificationSummary = verification.Summary
        };
        await _connectionRepository.UpdateAsync(updated, cancellationToken);
        return updated;
    }

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
