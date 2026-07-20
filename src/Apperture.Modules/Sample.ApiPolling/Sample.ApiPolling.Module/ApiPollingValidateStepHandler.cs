using Apperture.Core.Application.Execution;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

public sealed class ApiPollingValidateStepHandler : IStepHandler
{
    public ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        _ = context;
        _ = cancellationToken;

        var snapshotId = ApiPollingParameterReader.GetString(parameters, "snapshotId");
        if (string.IsNullOrWhiteSpace(snapshotId))
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                "Sample.ApiPolling requires procedure parameter snapshotId.");
        }

        if (!ApiPollingSnapshotIds.IsKnown(snapshotId))
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                $"Unknown snapshot id '{snapshotId}'. Known: {string.Join(", ", ApiPollingSnapshotIds.All)}.");
        }

        var retryMode = ApiPollingParameterReader.ResolveRetryMode(parameters);
        if (retryMode)
        {
            var sequence = ApiPollingParameterReader.GetInt(
                parameters,
                ApiPollingOperationItemRetryHandler.RetrySequenceParameter);
            if (sequence is null or < 1)
            {
                throw OperatorExecutionResult.FailSummaryOnly(
                    "ApiPolling retry mode requires retrySequence >= 1.");
            }

            if (string.IsNullOrWhiteSpace(
                    ApiPollingParameterReader.GetString(
                        parameters,
                        ApiPollingOperationItemRetryHandler.ParentOperationItemIdParameter)))
            {
                throw OperatorExecutionResult.FailSummaryOnly(
                    "ApiPolling retry mode requires parentOperationItemId.");
            }

            if (string.IsNullOrWhiteSpace(
                    ApiPollingParameterReader.GetString(
                        parameters,
                        ApiPollingOperationItemRetryHandler.RetryAttemptIdParameter)))
            {
                throw OperatorExecutionResult.FailSummaryOnly(
                    "ApiPolling retry mode requires retryAttemptId.");
            }
        }

        var pageSize = ApiPollingParameterReader.ResolvePageSize(parameters);
        var maxRecords = ApiPollingParameterReader.ResolveMaxRecords(parameters);
        if (pageSize <= 0)
            throw OperatorExecutionResult.FailSummaryOnly("pageSize must be greater than zero.");
        if (maxRecords <= 0)
            throw OperatorExecutionResult.FailSummaryOnly("maxRecordsPerSync must be greater than zero.");

        return ValueTask.FromResult<object?>(
            OperatorExecutionResult.SuccessSummaryOnly(new
            {
                step = "Validate",
                snapshotId = snapshotId.Trim(),
                retryMode,
                resetState = ApiPollingParameterReader.ResolveResetState(parameters),
                strictMode = ApiPollingParameterReader.ResolveStrictMode(parameters),
                failOnRecordError = ApiPollingParameterReader.ResolveFailOnRecordError(parameters),
                includeDeleted = ApiPollingParameterReader.ResolveIncludeDeleted(parameters),
                pageSize,
                maxRecordsPerSync = maxRecords,
            }));
    }
}
