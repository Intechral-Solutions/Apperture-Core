namespace Apperture.Core.Application.Services;

/// <summary>
/// Provides the scheduler host identity for multi-host claim coordination. Epic 36.
/// Register in the host (e.g. WorkerHost) to use RuntimeId or a dedicated SchedulerHostId.
/// </summary>
public interface ISchedulerHostIdentity
{
    string HostId { get; }
}

/// <summary>
/// Default implementation. Use when no host-specific identity is configured.
/// </summary>
public sealed class DefaultSchedulerHostIdentity : ISchedulerHostIdentity
{
    public DefaultSchedulerHostIdentity(string hostId = "default") => HostId = hostId;
    public string HostId { get; }
}
