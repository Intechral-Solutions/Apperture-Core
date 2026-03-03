namespace Apperture.Instance.Agent;

public sealed class InstanceAgentStub : IInstanceAgent
{
    public Task<InstanceStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new InstanceStatus(true, "Stub"));
}
