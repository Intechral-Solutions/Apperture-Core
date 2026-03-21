using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Scheduling;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// SQL-backed schedule management. Epic 28.
/// </summary>
public sealed class SqlScheduleManagementService : IScheduleManagementService
{
    private readonly IScheduleRepository _repo;

    public SqlScheduleManagementService(IScheduleRepository repo) => _repo = repo;

    public async ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken cancellationToken = default) =>
        await _repo.GetDefinitionAsync(id, cancellationToken);

    public async ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default) =>
        await _repo.ListDefinitionsAsync(cancellationToken);

    public async ValueTask<ScheduleDefinition> CreateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default)
    {
        await _repo.AddDefinitionAsync(definition, cancellationToken);
        return definition;
    }

    public async ValueTask UpdateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default)
    {
        await _repo.UpdateDefinitionAsync(definition, cancellationToken);
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
        var updated = def with { Enabled = true };
        await _repo.UpdateDefinitionAsync(updated, cancellationToken);
    }

    public async ValueTask DisableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default)
    {
        var def = await _repo.GetDefinitionAsync(scheduleId, cancellationToken)
            ?? throw new InvalidOperationException($"Schedule {scheduleId.Value} not found.");
        var updated = def with { Enabled = false };
        await _repo.UpdateDefinitionAsync(updated, cancellationToken);
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
        return new ScheduleWithExecutionState(def, state);
    }
}
