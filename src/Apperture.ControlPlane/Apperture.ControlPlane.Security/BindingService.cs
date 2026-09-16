using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Binding service with authorization and eligibility enforcement.
/// </summary>
public sealed class BindingService : IBindingService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;
    private readonly IBindingRepository _bindingRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IBindingEligibilityService _eligibilityService;
    private readonly IIntegrationRepository _integrationRepository;
    private readonly AppertureDbContext _db;
    private readonly IResourceOwnershipMutationGate _ownershipGate;

    public BindingService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IBindingRepository bindingRepository,
        IConnectionRepository connectionRepository,
        IBindingEligibilityService eligibilityService,
        IIntegrationRepository integrationRepository,
        AppertureDbContext db,
        IResourceOwnershipMutationGate ownershipGate)
    {
        _principalContext = principalContext;
        _auth = auth;
        _bindingRepository = bindingRepository;
        _connectionRepository = connectionRepository;
        _eligibilityService = eligibilityService;
        _integrationRepository = integrationRepository;
        _db = db;
        _ownershipGate = ownershipGate;
    }

    /// <inheritdoc />
    public async ValueTask<Binding> CreateBindingAsync(
        string moduleId,
        string slotName,
        string contractType,
        ConnectionId connectionId,
        IntegrationId integrationId,
        CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // EnableRetryOnFailure is active: every attempt must be self-contained.
            _db.ChangeTracker.Clear();

            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            // Design gate §8.7 / product decision P1: creating a binding asserts a cross-context
            // relationship, so it is an ownership-sensitive mutation and takes the gate FIRST -- before
            // reading either row. A concurrent Integration move, Connection move or archive is therefore
            // either entirely before or entirely after this transaction, never interleaved with it.
            await _ownershipGate.AcquireAsync(cancellationToken).ConfigureAwait(false);

            var integration = await _integrationRepository.GetByIdForUpdateAsync(integrationId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Integration {integrationId.Value} not found.");
            if (integration.ModuleId != moduleId)
                throw new InvalidOperationException($"Integration module {integration.ModuleId} does not match binding module {moduleId}.");

            var identity = integration.GetIdentity();
            var allowedIntegration = await _auth.EvaluateAsync(
                actor,
                new PermissionDescriptor(ActionConstants.IntegrationUpdate, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity,
                cancellationToken).ConfigureAwait(false);
            if (!allowedIntegration)
                throw new UnauthorizedAccessException("Not authorized to manage bindings for this integration.");

            var existing = await _bindingRepository.GetBySlotAndIntegrationAsync(moduleId, slotName, integrationId, cancellationToken).ConfigureAwait(false);
            if (existing is not null && existing.State == BindingState.Active)
                throw new InvalidOperationException($"Slot {slotName} is already bound for this integration.");

            var slotScopeKey = ScopeKeyConstants.Slot(moduleId, slotName);
            var allowed = await _auth.EvaluateAsync(
                actor,
                new PermissionDescriptor(ActionConstants.BindingAssign, new ResourceDescriptor(ResourceTypeConstants.Binding, slotScopeKey)),
                null, null, cancellationToken).ConfigureAwait(false);
            if (!allowed)
                throw new UnauthorizedAccessException("Not authorized to assign bindings to this slot.");

            // Authoritative ResourceContext reference check against a fresh, locked read of the Connection
            // row, inside the gate. No concurrent move can have committed between this read and this
            // transaction's own commit.
            var connection = await _connectionRepository.GetByIdForUpdateAsync(connectionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Connection {connectionId.Value} not found.");
            if (!ResourceContextReferenceRule.CanReference(integration.ResourceContextId, connection.ResourceContextId))
            {
                throw new InvalidOperationException(
                    $"Connection {connectionId.Value} belongs to ResourceContext '{connection.ResourceContextId.Value}', which is neither this Integration's ResourceContext ('{integration.ResourceContextId.Value}') nor Shared.");
            }

            var eligible = await _eligibilityService.GetEligibleConnectionsAsync(
                moduleId, slotName, contractType, integration.ResourceContextId, cancellationToken).ConfigureAwait(false);
            if (!eligible.Any(c => c.Id.Value == connectionId.Value))
                throw new InvalidOperationException($"Connection {connectionId.Value} is not eligible for binding to slot {moduleId}:{slotName}.");

            var bindingId = new BindingId(Guid.NewGuid().ToString("N")[..24]);
            var created = new Binding(bindingId, integrationId, moduleId, slotName, contractType, connectionId, null, BindingState.Active);
            await _bindingRepository.AddAsync(created, cancellationToken).ConfigureAwait(false);

            if (tx is not null)
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return created;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<Binding> CreateCapabilityBindingAsync(
        string moduleId,
        string slotName,
        string contractType,
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var slotScopeKey = ScopeKeyConstants.Slot(moduleId, slotName);
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.BindingAssign, new ResourceDescriptor(ResourceTypeConstants.Binding, slotScopeKey)),
            null,
            null,
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to assign bindings to this slot.");

        var id = new BindingId(Guid.NewGuid().ToString("N")[..24]);
        var binding = new Binding(id, null, moduleId, slotName, contractType, null, capabilityId, BindingState.Active);

        await _bindingRepository.AddAsync(binding, cancellationToken);
        return binding;
    }

    /// <inheritdoc />
    public async ValueTask DeleteBindingAsync(BindingId bindingId, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var binding = await _bindingRepository.GetByIdAsync(bindingId, cancellationToken);
        if (binding is null)
            return;

        var identity = binding.GetIdentity();
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.BindingRevoke, GovernedResourceDescriptor.FromIdentity(identity)),
            GovernedResourceDescriptor.FromIdentity(identity),
            identity,
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to revoke this binding.");

        await _bindingRepository.DeleteAsync(bindingId, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Binding?> GetBindingAsync(string moduleId, string slotName, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var slotScopeKey = ScopeKeyConstants.Slot(moduleId, slotName);
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.BindingRead, new ResourceDescriptor(ResourceTypeConstants.Binding, slotScopeKey)),
            null,
            null,
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to view bindings for this slot.");

        // Module-scoped surface: only a binding that no Integration owns may be resolved here.
        var binding = await _bindingRepository.GetUnownedBySlotAsync(moduleId, slotName, cancellationToken);
        return binding?.State == BindingState.Active ? binding : null;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Connection>> GetEligibleConnectionsAsync(
        string moduleId,
        string slotName,
        string contractType,
        ResourceContextId? owningResourceContextId = null,
        CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var slotScopeKey = ScopeKeyConstants.Slot(moduleId, slotName);
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.BindingAssign, new ResourceDescriptor(ResourceTypeConstants.Binding, slotScopeKey)),
            null,
            null,
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to view eligible connections for this slot.");

        return await _eligibilityService.GetEligibleConnectionsAsync(
            moduleId,
            slotName,
            contractType,
            owningResourceContextId,
            cancellationToken);
    }
}
