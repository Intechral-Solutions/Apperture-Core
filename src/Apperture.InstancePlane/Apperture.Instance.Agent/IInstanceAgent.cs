namespace Apperture.Instance.Agent;

public interface IInstanceAgent
{
    Task<InstanceStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

public sealed record InstanceStatus(bool IsHealthy, string? Message);
