using Apperture.Management.Client;

namespace Apperture.Management.Ui.Client.Execution;

public interface IExecutionLaunchAdapter
{
    string Kind { get; }

    Task<ExecutionLaunchOutcome> LaunchAsync(
        IManagementClient client,
        ExecutionDescriptor descriptor,
        IReadOnlyDictionary<string, object?>? parameters,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowExecutionLaunchAdapter : IExecutionLaunchAdapter
{
    public string Kind => ExecutionTargetKinds.Workflow;

    public async Task<ExecutionLaunchOutcome> LaunchAsync(
        IManagementClient client,
        ExecutionDescriptor descriptor,
        IReadOnlyDictionary<string, object?>? parameters,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var result = await client.StartWorkflowAsync(
            descriptor.TargetId,
            descriptor.IntegrationId,
            parameters,
            idempotencyKey,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        return new ExecutionLaunchOutcome(true, result.RunId, result.Status, null, null);
    }
}

public sealed class OperationExecutionLaunchAdapter : IExecutionLaunchAdapter
{
    public string Kind => ExecutionTargetKinds.Operation;

    public async Task<ExecutionLaunchOutcome> LaunchAsync(
        IManagementClient client,
        ExecutionDescriptor descriptor,
        IReadOnlyDictionary<string, object?>? parameters,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var result = await client.StartOperationAsync(
            descriptor.TargetId,
            descriptor.IntegrationId,
            parameters,
            idempotencyKey,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        return new ExecutionLaunchOutcome(true, result.RunId, result.Status, null, null);
    }
}

public sealed class ProcedureExecutionLaunchAdapter : IExecutionLaunchAdapter
{
    public string Kind => ExecutionTargetKinds.Procedure;

    public async Task<ExecutionLaunchOutcome> LaunchAsync(
        IManagementClient client,
        ExecutionDescriptor descriptor,
        IReadOnlyDictionary<string, object?>? parameters,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var procedureId = descriptor.ProcedureDetail?.Id ?? descriptor.TargetId;
        var result = await client.LaunchIntegrationProcedureAsync(
            descriptor.IntegrationId,
            procedureId,
            parameters,
            idempotencyKey,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded || result.Result is null)
        {
            return new ExecutionLaunchOutcome(
                false,
                null,
                null,
                result.ErrorCode,
                result.ErrorMessage ?? $"Launch failed with status code {result.StatusCode}.");
        }

        return new ExecutionLaunchOutcome(true, result.Result.RunId, result.Result.Status, null, null);
    }
}
