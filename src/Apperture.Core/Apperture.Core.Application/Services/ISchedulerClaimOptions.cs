namespace Apperture.Core.Application.Services;

/// <summary>
/// Scheduler claim lease configuration for multi-host coordination. Epic 36.
/// </summary>
public interface ISchedulerClaimOptions
{
    int ClaimLeaseMinutes { get; }
}

/// <summary>
/// Default implementation. Use when no host-specific configuration is provided.
/// </summary>
public sealed class DefaultSchedulerClaimOptions : ISchedulerClaimOptions
{
    public DefaultSchedulerClaimOptions(int claimLeaseMinutes = 5) => ClaimLeaseMinutes = claimLeaseMinutes;
    public int ClaimLeaseMinutes { get; }
}
