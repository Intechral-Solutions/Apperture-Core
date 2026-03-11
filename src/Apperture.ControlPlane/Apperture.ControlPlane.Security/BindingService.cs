using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

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

    public BindingService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IBindingRepository bindingRepository,
        IConnectionRepository connectionRepository,
        IBindingEligibilityService eligibilityService)
    {
        _principalContext = principalContext;
        _auth = auth;
        _bindingRepository = bindingRepository;
        _connectionRepository = connectionRepository;
        _eligibilityService = eligibilityService;
    }

    /// <inheritdoc />
    public async ValueTask<Binding> CreateBindingAsync(
        string moduleId,
        string slotName,
        string contractType,
        ConnectionId connectionId,
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

        var eligible = await _eligibilityService.GetEligibleConnectionsAsync(moduleId, slotName, contractType, cancellationToken);
        if (!eligible.Any(c => c.Id.Value == connectionId.Value))
            throw new InvalidOperationException($"Connection {connectionId.Value} is not eligible for binding to slot {moduleId}:{slotName}.");

        var id = new BindingId(Guid.NewGuid().ToString("N")[..24]);
        var binding = new Binding(id, moduleId, slotName, contractType, connectionId, null, BindingState.Active);

        await _bindingRepository.AddAsync(binding, cancellationToken);
        return binding;
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
        var binding = new Binding(id, moduleId, slotName, contractType, null, capabilityId, BindingState.Active);

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

        var binding = await _bindingRepository.GetBySlotAsync(moduleId, slotName, cancellationToken);
        return binding?.State == BindingState.Active ? binding : null;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Connection>> GetEligibleConnectionsAsync(
        string moduleId,
        string slotName,
        string contractType,
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

        return await _eligibilityService.GetEligibleConnectionsAsync(moduleId, slotName, contractType, cancellationToken);
    }
}
