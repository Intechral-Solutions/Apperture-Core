namespace Apperture.ControlPlane.Security;

/// <summary>
/// Governance checks for recovery/remediation operator flows. Epic 34.
/// </summary>
public interface IRecoveryGovernanceService
{
    ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default);
}
