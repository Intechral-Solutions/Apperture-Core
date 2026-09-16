using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Shared.Kernel;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Validates and performs explicit resource-ownership moves (design gate §8). CTX-3B Plan 1: preview
/// validation only. <c>Move*Async</c> mutation and locking land in Plan 2; both call the same
/// <c>Validate*MoveAsync</c> methods this plan defines, so preview and move cannot drift (§8.2).
/// </summary>
public sealed class ResourceOwnershipMoveService : IResourceOwnershipMoveService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IIntegrationRepository _integrationRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IScheduleRepository _scheduleRepository;
    private readonly IBindingRepository _bindingRepository;
    private readonly IResourceContextService _resourceContexts;

    public ResourceOwnershipMoveService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IIntegrationRepository integrationRepository,
        IConnectionRepository connectionRepository,
        IScheduleRepository scheduleRepository,
        IBindingRepository bindingRepository,
        IResourceContextService resourceContexts)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _integrationRepository = integrationRepository;
        _connectionRepository = connectionRepository;
        _scheduleRepository = scheduleRepository;
        _bindingRepository = bindingRepository;
        _resourceContexts = resourceContexts;
    }

    /// <inheritdoc />
    public async ValueTask<ResourceMovePreview> PreviewIntegrationMoveAsync(
        IntegrationId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default)
    {
        // House pattern (IntegrationManagementService.UpdateIntegrationAsync): resolve the actor before
        // touching the repository, so an unauthenticated caller cannot use the not-found/unauthorized
        // exception distinction as an existence oracle for ids it was never entitled to probe.
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        var integration = await _integrationRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Integration {id.Value} not found.");
        await AuthorizeAsync(actor, ActionConstants.IntegrationUpdate, integration.GetIdentity(), cancellationToken).ConfigureAwait(false);
        var validation = await ValidateIntegrationMoveAsync(integration, targetResourceContextId, cancellationToken).ConfigureAwait(false);
        return validation.Preview;
    }

    /// <inheritdoc />
    public async ValueTask<ResourceMovePreview> PreviewConnectionMoveAsync(
        ConnectionId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default)
    {
        // Same house pattern as PreviewIntegrationMoveAsync: resolve the actor before touching the
        // repository, so an unauthenticated caller cannot use the not-found/unauthorized exception
        // distinction as an existence oracle for ids it was never entitled to probe.
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        var connection = await _connectionRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Connection {id.Value} not found.");
        await AuthorizeAsync(actor, ActionConstants.ConnectionUpdate, connection.GetIdentity(), cancellationToken).ConfigureAwait(false);
        return await ValidateConnectionMoveAsync(connection, targetResourceContextId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared validator for Connection moves (§8.5). Frozen signature: Plan 2 calls this unchanged inside
    /// a locked transaction, so preview and move cannot drift (§8.2).
    /// </summary>
    /// <remarks>
    /// Direction note (design gate §8.5 step 3): for a Connection move, the bound Integration is the
    /// owner and stays put while the Connection becomes owned by the target, so the reference check is
    /// <c>CanReference(integration.ResourceContextId, targetId)</c> -- the opposite argument order from
    /// the Integration validator's <c>CanReference(targetId, connection.ResourceContextId)</c>, because in
    /// each case the argument order reflects whichever resource is the one actually moving.
    /// </remarks>
    private async ValueTask<ResourceMovePreview> ValidateConnectionMoveAsync(
        Connection connection, ResourceContextId targetId, CancellationToken cancellationToken)
    {
        var sourceId = connection.ResourceContextId;
        var sourceRef = await ResolveRefAsync(sourceId, cancellationToken).ConfigureAwait(false);
        var target = await _resourceContexts.GetAsync(targetId, cancellationToken).ConfigureAwait(false);

        var blockers = new List<ResourceMoveBlocker>();
        var effects = new List<ResourceMoveEffect>();

        if (target is null)
        {
            blockers.Add(new ResourceMoveBlocker(ResourceMoveBlockerCodes.TargetNotFound, $"ResourceContext '{targetId.Value}' does not exist.", null, null, null, null));
            var targetRef = new ResourceContextRef(targetId.Value, targetId.Value, targetId.Value);
            return BuildConnectionPreview(connection.Id.Value, sourceRef, targetRef, blockers, effects);
        }
        if (!target.IsActive)
        {
            blockers.Add(new ResourceMoveBlocker(ResourceMoveBlockerCodes.TargetArchived, $"'{target.Name}' is archived and cannot receive new ownership.", ResourceMoveResourceTypes.ResourceContext, target.Id.Value, target.Name, target.Id.Value));
            return BuildConnectionPreview(connection.Id.Value, sourceRef, target.ToRef(), blockers, effects);
        }
        if (string.Equals(sourceId.Value, targetId.Value, StringComparison.Ordinal))
        {
            blockers.Add(new ResourceMoveBlocker(ResourceMoveBlockerCodes.AlreadyInTarget, "The connection is already owned by the requested context.", null, null, null, null));
            return BuildConnectionPreview(connection.Id.Value, sourceRef, target.ToRef(), blockers, effects);
        }

        // Deterministic ordering (Global Constraints): GetByConnectionIdAsync has no ORDER BY.
        var bindings = (await _bindingRepository.GetByConnectionIdAsync(connection.Id, cancellationToken).ConfigureAwait(false))
            .OrderBy(b => b.Id.Value, StringComparer.Ordinal)
            .ToList();

        // De-duplicated by Integration id (review fix round 1, Important 2/Minor 3): a Connection bound
        // into two slots of the SAME Integration is an ordinary configuration, not two distinct blast-radius
        // entries -- both the "Used by" count and the unreachable blocker must describe each distinct
        // Integration once, never once per binding. A binding whose Integration row is missing is never
        // counted as "used by" at all -- the dangling edge is already surfaced via the advisory effect below,
        // and naming a phantom Integration in the blast-radius payload would mislead the operator.
        var usedByIntegrationIds = new List<string>();
        var seenUsedByIntegrationIds = new HashSet<string>(StringComparer.Ordinal);
        var blockedIntegrationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (binding.IntegrationId is { } integrationId)
            {
                var integration = await _integrationRepository.GetByIdAsync(integrationId, cancellationToken).ConfigureAwait(false);
                if (integration is null)
                {
                    // Mirror of the Integration validator's missing-connection advisory: surface the
                    // dangling edge, never silently skip it. The move cannot make this worse.
                    effects.Add(new ResourceMoveEffect(
                        ResourceMoveEffectCodes.BindingReferencesMissingIntegration,
                        $"Binding for slot '{binding.SlotName}' references a missing integration; the move does not make this worse.",
                        ResourceMoveResourceTypes.Binding, [binding.Id.Value]));
                    continue;
                }

                if (seenUsedByIntegrationIds.Add(integrationId.Value))
                    usedByIntegrationIds.Add(integrationId.Value);

                if (!ResourceContextReferenceRule.CanReference(integration.ResourceContextId, targetId)
                    && blockedIntegrationIds.Add(integrationId.Value))
                {
                    blockers.Add(new ResourceMoveBlocker(
                        ResourceMoveBlockerCodes.BindingIntegrationUnreachable,
                        $"Integration '{integration.DisplayName}' (in '{integration.ResourceContextId.Value}') would no longer be able to reference this connection.",
                        ResourceMoveResourceTypes.Integration, integration.Id.Value, integration.DisplayName, integration.ResourceContextId.Value));
                }
            }
            else if (!ResourceContextIds.IsShared(targetId))
            {
                blockers.Add(new ResourceMoveBlocker(
                    ResourceMoveBlockerCodes.UnownedBindingRequiresShared,
                    "A platform-level binding uses this connection; it must stay in Shared.",
                    ResourceMoveResourceTypes.Binding, binding.Id.Value, null, null));
            }
        }

        if (usedByIntegrationIds.Count > 0)
        {
            effects.Add(new ResourceMoveEffect(
                ResourceMoveEffectCodes.UsedByIntegration,
                $"Used by {usedByIntegrationIds.Count} integration(s).",
                ResourceMoveResourceTypes.Integration, usedByIntegrationIds));
        }

        return BuildConnectionPreview(connection.Id.Value, sourceRef, target.ToRef(), blockers, effects);
    }

    private static ResourceMovePreview BuildConnectionPreview(
        string resourceId, ResourceContextRef source, ResourceContextRef target,
        List<ResourceMoveBlocker> blockers, List<ResourceMoveEffect> effects) =>
        new(ResourceMoveResourceTypes.Connection, resourceId, source, target, blockers.Count == 0, blockers, effects);

    /// <summary>
    /// The Integration validator's full result: the operator-facing preview, plus the authoritative
    /// attached-Schedule set it was computed from. Plan 2 mutates exactly this set and Plan 4 audits
    /// exactly this set, so all three describe the same schedules from one read inside one transaction.
    /// Private: never part of the API or client contract.
    /// </summary>
    private sealed record IntegrationMoveValidation(
        ResourceMovePreview Preview,
        IReadOnlyList<ScheduleDefinition> AttachedSchedules);

    /// <summary>
    /// Shared validator for Integration moves (§8.4). Called by preview here and, unchanged, by the
    /// gated transaction in Plan 2 -- this is the mechanism that prevents preview/move drift (§8.2).
    /// </summary>
    private async ValueTask<IntegrationMoveValidation> ValidateIntegrationMoveAsync(
        DomainIntegration integration, ResourceContextId targetId, CancellationToken cancellationToken)
    {
        var sourceId = integration.ResourceContextId;
        var sourceRef = await ResolveRefAsync(sourceId, cancellationToken).ConfigureAwait(false);
        var target = await _resourceContexts.GetAsync(targetId, cancellationToken).ConfigureAwait(false);

        var blockers = new List<ResourceMoveBlocker>();
        var effects = new List<ResourceMoveEffect>();

        if (target is null)
        {
            blockers.Add(new ResourceMoveBlocker(
                ResourceMoveBlockerCodes.TargetNotFound, $"ResourceContext '{targetId.Value}' does not exist.",
                null, null, null, null));
            // No second lookup here: target is already known to be null, so the ref is necessarily the
            // synthetic fallback -- re-querying via ResolveRefAsync would only repeat the same null result.
            var targetRef = new ResourceContextRef(targetId.Value, targetId.Value, targetId.Value);
            return new(BuildIntegrationPreview(integration.Id.Value, sourceRef, targetRef, blockers, effects), []);
        }
        if (!target.IsActive)
        {
            blockers.Add(new ResourceMoveBlocker(
                ResourceMoveBlockerCodes.TargetArchived, $"'{target.Name}' is archived and cannot receive new ownership.",
                ResourceMoveResourceTypes.ResourceContext, target.Id.Value, target.Name, target.Id.Value));
            return new(BuildIntegrationPreview(integration.Id.Value, sourceRef, target.ToRef(), blockers, effects), []);
        }
        if (string.Equals(sourceId.Value, targetId.Value, StringComparison.Ordinal))
        {
            blockers.Add(new ResourceMoveBlocker(
                ResourceMoveBlockerCodes.AlreadyInTarget, "The integration is already owned by the requested context.",
                null, null, null, null));
            return new(BuildIntegrationPreview(integration.Id.Value, sourceRef, target.ToRef(), blockers, effects), []);
        }

        // Deterministic ordering (Global Constraints): the repository methods below have no ORDER BY, so
        // SQL Server row order is unspecified. Sorting here is what makes the blocker/effect lists stable
        // for the API response, the UI dialog, and the audit payload.
        var bindings = (await _bindingRepository.GetByIntegrationIdAsync(integration.Id, cancellationToken).ConfigureAwait(false))
            .OrderBy(b => b.Id.Value, StringComparer.Ordinal)
            .ToList();

        // De-duplicated by Connection id (review fix round 1, Important 2, mirror of the Connection
        // validator's fix): an Integration bound to the SAME Connection across two slots must produce one
        // blocker for that Connection, not one per binding. Distinct Connections still each keep their own
        // blocker -- see Multiple_unreachable_connections_each_produce_their_own_blocker.
        var blockedConnectionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (binding.ConnectionId is not { } connectionId)
                continue; // capability binding: no Connection to check.

            var connection = await _connectionRepository.GetByIdAsync(connectionId, cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                effects.Add(new ResourceMoveEffect(
                    ResourceMoveEffectCodes.BindingReferencesMissingConnection,
                    $"Binding for slot '{binding.SlotName}' references a missing connection; the move does not make this worse.",
                    ResourceMoveResourceTypes.Binding, [binding.Id.Value]));
                continue;
            }

            if (!ResourceContextReferenceRule.CanReference(targetId, connection.ResourceContextId)
                && blockedConnectionIds.Add(connection.Id.Value))
            {
                blockers.Add(new ResourceMoveBlocker(
                    ResourceMoveBlockerCodes.BindingConnectionUnreachable,
                    $"Connection '{connection.Name}' is owned by '{connection.ResourceContextId.Value}' and would become unreachable if this integration moved to '{target.Name}'.",
                    ResourceMoveResourceTypes.Connection, connection.Id.Value, connection.Name, connection.ResourceContextId.Value));
            }
        }

        // The authoritative attached-Schedule set. Read exactly once; returned to the caller so Plan 2's
        // mutation and Plan 4's audit describe the same schedules this preview described.
        var schedules = (await _scheduleRepository.GetByIntegrationIdAsync(integration.Id, cancellationToken).ConfigureAwait(false))
            .OrderBy(s => s.Id.Value, StringComparer.Ordinal)
            .ToList();

        // NOTE (verified at review time): SqlScheduleRepository.ToDefinition coalesces a null/whitespace
        // ResourceContextId column to Shared, so `s.ResourceContextId` is never null coming out of the
        // repository. The null test below is retained only as a defensive guard against a future mapper
        // change -- it is not the mechanism that catches legacy NULL rows. Legacy NULL rows present as
        // Shared and therefore land in the `!= sourceId` branch. See this plan's "Legacy schedule
        // ownership drift" note and Plan 2's preflight (product decision P3).
        var inconsistent = schedules
            .Where(s => s.ResourceContextId is null || !string.Equals(s.ResourceContextId.Value, sourceId.Value, StringComparison.Ordinal))
            .ToList();
        if (inconsistent.Count > 0)
        {
            foreach (var s in inconsistent)
            {
                blockers.Add(new ResourceMoveBlocker(
                    ResourceMoveBlockerCodes.ScheduleContextInconsistent,
                    $"Schedule '{s.Name}' does not currently match this integration's context and must be corrected before it can move.",
                    ResourceMoveResourceTypes.Schedule, s.Id.Value, s.Name, s.ResourceContextId?.Value));
            }
        }
        else if (schedules.Count > 0)
        {
            effects.Add(new ResourceMoveEffect(
                ResourceMoveEffectCodes.SchedulesMoveWithIntegration,
                $"{schedules.Count} schedule(s) move with this integration.",
                ResourceMoveResourceTypes.Schedule, schedules.Select(s => s.Id.Value).ToList()));
        }

        return new(BuildIntegrationPreview(integration.Id.Value, sourceRef, target.ToRef(), blockers, effects), schedules);
    }

    /// <summary>
    /// Resolves the current actor, failing fast if there is none. Shared by every <c>Preview*</c>/<c>Move*</c>
    /// method (Tasks 5 and 6 reuse this for the Connection and Schedule families): callers must resolve the
    /// actor <em>before</em> loading the target resource, matching the house pattern
    /// (<c>IntegrationManagementService.UpdateIntegrationAsync</c>) -- otherwise an unauthenticated caller
    /// could distinguish "not found" from "unauthorized" purely from exception type, which leaks resource
    /// existence to callers who were never authenticated at all.
    /// </summary>
    private async ValueTask<IActor> RequireActorAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        return actor ?? throw new UnauthorizedAccessException("No authenticated actor.");
    }

    /// <summary>
    /// Evaluates whether <paramref name="actor"/> (already resolved via <see cref="RequireActorAsync"/>)
    /// holds <paramref name="action"/> against <paramref name="identity"/>. Shared by every move family.
    /// </summary>
    private async ValueTask AuthorizeAsync(IActor actor, string action, IGovernedResourceIdentity identity, CancellationToken cancellationToken)
    {
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(action, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity),
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException($"Not authorized to move this resource ({action}).");
    }

    private async ValueTask<ResourceContextRef> ResolveRefAsync(ResourceContextId id, CancellationToken cancellationToken)
    {
        var ctx = await _resourceContexts.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return ctx?.ToRef() ?? new ResourceContextRef(id.Value, id.Value, id.Value);
    }

    private static ResourceMovePreview BuildIntegrationPreview(
        string resourceId, ResourceContextRef source, ResourceContextRef target,
        List<ResourceMoveBlocker> blockers, List<ResourceMoveEffect> effects) =>
        new(ResourceMoveResourceTypes.Integration, resourceId, source, target, blockers.Count == 0, blockers, effects);
}
