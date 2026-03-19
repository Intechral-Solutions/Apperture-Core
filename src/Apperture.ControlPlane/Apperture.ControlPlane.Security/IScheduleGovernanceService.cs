namespace Apperture.ControlPlane.Security;

public interface IScheduleGovernanceService
{
    ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default);
}

