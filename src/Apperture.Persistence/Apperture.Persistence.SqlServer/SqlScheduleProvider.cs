using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Scheduling;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// SQL-backed schedule provider. Epic 28.
/// </summary>
public sealed class SqlScheduleProvider : IScheduleProvider
{
    private readonly IScheduleRepository _repo;

    public SqlScheduleProvider(IScheduleRepository repo) => _repo = repo;

    public async IAsyncEnumerable<ScheduleAssignment> GetAssignmentsAsync(RuntimePlaneId runtimePlaneId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var assignments = await _repo.GetAssignmentsAsync(runtimePlaneId, cancellationToken);
        foreach (var a in assignments)
        {
            var def = await _repo.GetDefinitionAsync(a.ScheduleId, cancellationToken);
            if (def is not null && def.Enabled)
                yield return a;
        }
    }
}
