using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// SQL-backed schedule management. Epic 28, Epic 36. Epic 66: lifecycle evidence on write paths.
/// </summary>
public sealed class SqlScheduleManagementService : IScheduleManagementService
{
    private readonly IScheduleRepository _repo;
    private readonly ISchedulerClaimOptions _claimOptions;
    private readonly TimeProvider _timeProvider;
    private readonly IResourceContextService _resourceContexts;
    private readonly IIntegrationRepository _integrationRepository;

    public SqlScheduleManagementService(
        IScheduleRepository repo,
        ISchedulerClaimOptions claimOptions,
        TimeProvider timeProvider,
        IResourceContextService resourceContexts,
        IIntegrationRepository integrationRepository)
    {
        _repo = repo;
        _claimOptions = claimOptions;
        _timeProvider = timeProvider;
        _resourceContexts = resourceContexts;
        _integrationRepository = integrationRepository;
    }

    public async ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken cancellationToken = default) =>
        await _repo.GetDefinitionAsync(id, cancellationToken);

    public async ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default) =>
        await _repo.ListDefinitionsAsync(cancellationToken);

    public async ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(
        ResourceContextId? resourceContextId,
        CancellationToken cancellationToken = default) =>
        await _repo.ListDefinitionsAsync(resourceContextId, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<ScheduleDefinition> CreateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default)
    {
        var owningContextId = await ResolveOwningResourceContextAsync(definition, cancellationToken).ConfigureAwait(false);

        var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(
            null,
            definition with { ResourceContextId = owningContextId },
            _timeProvider.GetUtcNow());
        await _repo.AddDefinitionAsync(merged, cancellationToken);
        return merged;
    }

    /// <summary>
    /// Authoritative Schedule ownership rule. See <see cref="IScheduleManagementService.CreateScheduleAsync"/>.
    /// </summary>
    private async ValueTask<ResourceContextId> ResolveOwningResourceContextAsync(
        ScheduleDefinition definition,
        CancellationToken cancellationToken)
    {
        if (definition.IntegrationId is not { } integrationId)
        {
            // Standalone schedule: no Integration to inherit from. Default to Shared.
            var standaloneContext = await _resourceContexts
                .ResolveForNewOwnershipAsync(definition.ResourceContextId, cancellationToken)
                .ConfigureAwait(false);
            return standaloneContext.Id;
        }

        var integration = await _integrationRepository.GetByIdAsync(integrationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Integration {integrationId.Value} not found.");

        if (definition.ResourceContextId is null)
        {
            // Inherit. Never blindly default an Integration-attached schedule to Shared.
            // Reactivation guard (§6.5, D2): inheriting an archived context is not itself
            // resolved through ResolveForNewOwnershipAsync (unlike the other two branches), so it
            // needs its own Active check. Only fires when the caller is asking for a live (Enabled)
            // schedule -- creating a disabled one under an archived context makes nothing live and
            // must remain allowed.
            if (definition.Enabled)
            {
                await ResourceContextReactivationGuard.EnsureOwningContextIsActiveAsync(
                    integration.ResourceContextId, _resourceContexts, "creating an enabled schedule for this integration", cancellationToken).ConfigureAwait(false);
            }

            return integration.ResourceContextId;
        }

        if (!string.Equals(definition.ResourceContextId.Value, integration.ResourceContextId.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Schedule ResourceContext '{definition.ResourceContextId.Value}' does not match the owning Integration's ResourceContext '{integration.ResourceContextId.Value}'. An Integration-attached schedule belongs to its Integration's ResourceContext.");
        }

        // Explicit and matching: still confirm the context exists and is Active before taking ownership.
        var resolved = await _resourceContexts
            .ResolveForNewOwnershipAsync(definition.ResourceContextId, cancellationToken)
            .ConfigureAwait(false);
        return resolved.Id;
    }

    public async ValueTask UpdateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default)
    {
        var previous = await _repo.GetDefinitionAsync(definition.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Schedule {definition.Id.Value} not found.");

        // Reactivation guard (§6.5, D2), CTX-3A finding C2: PATCH /schedules/{id} reaches this method
        // directly and previously bypassed EnableAsync's guard entirely, letting an archived
        // Application's schedule be re-enabled through an ordinary field update. Flipping
        // Enabled false -> true here is the exact same reactivation EnableAsync guards, so it gets the
        // same guard. Any other field update (rename, cron change, disabling, staying disabled, or
        // staying enabled) remains allowed regardless of the owning context's archived state, since
        // none of those make anything newly live.
        if (!previous.Enabled && definition.Enabled && previous.ResourceContextId is { } owningContextId)
        {
            await ResourceContextReactivationGuard.EnsureOwningContextIsActiveAsync(
                owningContextId, _resourceContexts, "enabling this schedule", cancellationToken).ConfigureAwait(false);
        }

        var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(
            previous,
            definition,
            _timeProvider.GetUtcNow());
        await _repo.UpdateDefinitionAsync(merged, cancellationToken);
    }

    public async ValueTask<ScheduleAssignment> AssignToRuntimePlaneAsync(ScheduleId scheduleId, RuntimePlaneId runtimePlaneId, string? policyOverrides, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N")[..24];
        var assignment = new ScheduleAssignment(
            new ScheduleAssignmentId(id),
            scheduleId,
            runtimePlaneId,
            policyOverrides,
            DesiredVersion: 1,
            AppliedVersion: 0);
        await _repo.AddAssignmentAsync(assignment, cancellationToken);
        return assignment;
    }

    public async ValueTask EnableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default)
    {
        var def = await _repo.GetDefinitionAsync(scheduleId, cancellationToken)
            ?? throw new InvalidOperationException($"Schedule {scheduleId.Value} not found.");

        // Reactivation guard (§6.5, D2): while an Application ResourceContext is archived, nothing
        // it owns may become live again.
        if (def.ResourceContextId is { } owningContextId)
        {
            await ResourceContextReactivationGuard.EnsureOwningContextIsActiveAsync(
                owningContextId, _resourceContexts, "enabling this schedule", cancellationToken).ConfigureAwait(false);
        }

        var proposed = def with { Enabled = true };
        var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(def, proposed, _timeProvider.GetUtcNow());
        await _repo.UpdateDefinitionAsync(merged, cancellationToken);
    }

    public async ValueTask DisableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default)
    {
        var def = await _repo.GetDefinitionAsync(scheduleId, cancellationToken)
            ?? throw new InvalidOperationException($"Schedule {scheduleId.Value} not found.");
        var proposed = def with { Enabled = false };
        var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(def, proposed, _timeProvider.GetUtcNow());
        await _repo.UpdateDefinitionAsync(merged, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ScheduleAssignment>> GetAssignmentsAsync(RuntimePlaneId runtimePlaneId, CancellationToken cancellationToken = default)
    {
        return await _repo.GetAssignmentsAsync(runtimePlaneId, cancellationToken);
    }

    public async ValueTask<ScheduleWithExecutionState?> GetScheduleWithExecutionStateAsync(ScheduleId id, RuntimePlaneId planeId, CancellationToken cancellationToken = default)
    {
        var def = await _repo.GetDefinitionAsync(id, cancellationToken);
        if (def is null)
            return null;
        var assignments = await _repo.GetAssignmentsAsync(planeId, cancellationToken);
        var assignment = assignments.FirstOrDefault(a => a.ScheduleId.Value == id.Value);
        if (assignment is null)
            return new ScheduleWithExecutionState(def, null);
        var state = await _repo.GetStateAsync(assignment.Id.Value, cancellationToken);
        ScheduleInProgressClaim? claim = null;
        var claimInfo = await _repo.GetInProgressRunClaimAsync(assignment.Id.Value, cancellationToken);
        if (claimInfo.HasValue)
        {
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_claimOptions.ClaimLeaseMinutes);
            claim = new ScheduleInProgressClaim(
                claimInfo.Value.ClaimedByHostId,
                claimInfo.Value.StartedAtUtc,
                claimInfo.Value.StartedAtUtc < cutoff,
                claimInfo.Value.ResourceContextId);
        }
        return new ScheduleWithExecutionState(def, state, claim);
    }
}
