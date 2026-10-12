using Apperture.Core.Application.Audit;
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
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public BindingService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IBindingRepository bindingRepository,
        IConnectionRepository connectionRepository,
        IBindingEligibilityService eligibilityService,
        IIntegrationRepository integrationRepository,
        AppertureDbContext db,
        IResourceOwnershipMutationGate ownershipGate,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
    {
        _principalContext = principalContext;
        _auth = auth;
        _bindingRepository = bindingRepository;
        _connectionRepository = connectionRepository;
        _eligibilityService = eligibilityService;
        _integrationRepository = integrationRepository;
        _db = db;
        _ownershipGate = ownershipGate;
        _auditedUnitOfWork = auditedUnitOfWork;
    }

    private IAuditedUnitOfWork Unit(string what) =>
        _auditedUnitOfWork ?? throw new AuditInvariantException($"{what} requires the audited unit of work.");

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 4). Inside the attempt, in the order every ownership-sensitive writer uses
    /// (design gate §8.7, product decision P1): the ownership gate, the integration row (locked to the end of the transaction, the
    /// row an integration delete, an ownership move and every writer that refers to the integration also take), the module check,
    /// <c>integration.update</c> and <c>binding.assign</c>, the "already bound" check, the connection row (locked) with the
    /// ResourceContext reference rule and eligibility, then <c>binding.created</c> and the row. The binding id is chosen before
    /// the unit, so a rerun after a confirmed rollback creates the same binding and a verified commit after a lost
    /// acknowledgement is reconstructed from it.</para>
    /// <para>There is no unique index on a slot: two creates for one slot are serialized by the integration row lock, and the
    /// second finds the first's binding and is refused.</para>
    /// </remarks>
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

        var unit = Unit("Binding creation");
        var bindingId = new BindingId(Guid.NewGuid().ToString("N")[..24]);
        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.BindingCreated, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                // Design gate §8.7 / product decision P1: creating a binding asserts a cross-context relationship, so it is an
                // ownership-sensitive mutation and takes the gate FIRST -- before reading either row. A concurrent Integration
                // move, Connection move or archive is therefore either entirely before or entirely after this transaction.
                await _ownershipGate.AcquireAsync(ct).ConfigureAwait(false);

                var integration = await _integrationRepository.GetByIdForUpdateAsync(integrationId, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Integration {integrationId.Value} not found.");
                if (integration.ModuleId != moduleId)
                    throw new InvalidOperationException($"Integration module {integration.ModuleId} does not match binding module {moduleId}.");

                var identity = integration.GetIdentity();
                var allowedIntegration = await _auth.EvaluateAsync(
                    actor,
                    new PermissionDescriptor(ActionConstants.IntegrationUpdate, GovernedResourceDescriptor.FromIdentity(identity)),
                    GovernedResourceDescriptor.FromIdentity(identity),
                    identity,
                    ct).ConfigureAwait(false);
                if (!allowedIntegration)
                    throw new UnauthorizedAccessException("Not authorized to manage bindings for this integration.");

                var existing = await _bindingRepository.GetBySlotAndIntegrationAsync(moduleId, slotName, integrationId, ct).ConfigureAwait(false);
                if (existing is not null && existing.State == BindingState.Active)
                    throw new InvalidOperationException($"Slot {slotName} is already bound for this integration.");

                var slotScopeKey = ScopeKeyConstants.Slot(moduleId, slotName);
                var allowed = await _auth.EvaluateAsync(
                    actor,
                    new PermissionDescriptor(ActionConstants.BindingAssign, new ResourceDescriptor(ResourceTypeConstants.Binding, slotScopeKey)),
                    null, null, ct).ConfigureAwait(false);
                if (!allowed)
                    throw new UnauthorizedAccessException("Not authorized to assign bindings to this slot.");

                // Authoritative ResourceContext reference check against a fresh, locked read of the Connection row, inside the
                // gate. No concurrent move can have committed between this read and this transaction's own commit.
                var connection = await _connectionRepository.GetByIdForUpdateAsync(connectionId, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Connection {connectionId.Value} not found.");
                if (!ResourceContextReferenceRule.CanReference(integration.ResourceContextId, connection.ResourceContextId))
                {
                    throw new InvalidOperationException(
                        $"Connection {connectionId.Value} belongs to ResourceContext '{connection.ResourceContextId.Value}', which is neither this Integration's ResourceContext ('{integration.ResourceContextId.Value}') nor Shared.");
                }

                var eligible = await _eligibilityService.GetEligibleConnectionsAsync(
                    moduleId, slotName, contractType, integration.ResourceContextId, ct).ConfigureAwait(false);
                if (!eligible.Any(c => c.Id.Value == connectionId.Value))
                    throw new InvalidOperationException($"Connection {connectionId.Value} is not eligible for binding to slot {moduleId}:{slotName}.");

                var created = new Binding(bindingId, integrationId, moduleId, slotName, contractType, connectionId, null, BindingState.Active);
                await transaction.StageAsync(
                    AuditEvents.BindingCreated(
                        bindingId.Value, integrationId.Value, moduleId, slotName, connectionId.Value, capabilityId: null, integration.ResourceContextId.Value),
                    ct).ConfigureAwait(false);
                await _bindingRepository.AddAsync(created, ct).ConfigureAwait(false);
                return created;
            },
            async (committed, ct) => await _bindingRepository.GetByIdAsync(bindingId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Binding {bindingId.Value} could not be read back."),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One audited unit (AUD-1.4 slice 4). A binding with no owning integration has no row to serialize on and no ownership to
    /// protect, so the attempt is the authorization already made, <c>binding.created</c> and the row.
    /// </remarks>
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

        var unit = Unit("Binding creation");
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
        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.BindingCreated, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var binding = new Binding(id, null, moduleId, slotName, contractType, null, capabilityId, BindingState.Active);
                await transaction.StageAsync(
                    AuditEvents.BindingCreated(id.Value, integrationId: null, moduleId, slotName, connectionId: null, capabilityId, resourceContextId: null),
                    ct).ConfigureAwait(false);
                await _bindingRepository.AddAsync(binding, ct).ConfigureAwait(false);
                return binding;
            },
            async (committed, ct) => await _bindingRepository.GetByIdAsync(id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Binding {id.Value} could not be read back."),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 4). An early read and <c>binding.revoke</c> check answer a missing or forbidden
    /// request before a unit opens; a missing binding is still a silent return, as it always was (the HTTP routes answer 404 or
    /// 204 themselves). Inside the attempt: the owning integration's row, locked, which serializes every mutation of that
    /// integration's bindings (a create, another delete, a move, an integration delete); then the binding's own row, locked;
    /// <c>binding.revoke</c> again; <c>binding.deleted</c> and the removal. The delete takes no ownership gate: removing a
    /// binding makes nothing archive-blocking.</para>
    /// <para>A binding another request deleted first is a no-op that records nothing and returns quietly. A delete whose
    /// acknowledgement was lost is verified from the committed primary row and reported as the success it was, though the binding
    /// is now gone.</para>
    /// </remarks>
    public async ValueTask DeleteBindingAsync(BindingId bindingId, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var unit = Unit("Binding deletion");
        var binding = await _bindingRepository.GetByIdAsync(bindingId, cancellationToken);
        if (binding is null)
            return;
        await AuthorizeRevokeAsync(actor, binding, cancellationToken).ConfigureAwait(false);

        await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.BindingDeleted, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                string? resourceContextId = null;
                if (binding.IntegrationId is { } integrationId)
                {
                    var integration = await _integrationRepository.GetByIdForUpdateAsync(integrationId, ct).ConfigureAwait(false);
                    resourceContextId = integration?.ResourceContextId.Value;
                }

                var fresh = await _bindingRepository.GetByIdForUpdateAsync(bindingId, ct).ConfigureAwait(false);
                if (fresh is null)
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                await AuthorizeRevokeAsync(actor, fresh, ct).ConfigureAwait(false);
                await transaction.StageAsync(
                    AuditEvents.BindingDeleted(
                        fresh.Id.Value,
                        fresh.IntegrationId?.Value,
                        fresh.ModuleId,
                        fresh.SlotName,
                        fresh.ConnectionId?.Value,
                        fresh.CapabilityId,
                        resourceContextId),
                    ct).ConfigureAwait(false);
                await _bindingRepository.DeleteAsync(bindingId, ct).ConfigureAwait(false);
                return true;
            },
            // A verified commit is a successful delete, built from the committed primary row alone. The binding is gone by
            // design, so its absence is never read as a failed or missing delete.
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask AuthorizeRevokeAsync(IActor actor, Binding binding, CancellationToken cancellationToken)
    {
        var identity = binding.GetIdentity();
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.BindingRevoke, GovernedResourceDescriptor.FromIdentity(identity)),
            GovernedResourceDescriptor.FromIdentity(identity),
            identity,
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to revoke this binding.");
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
