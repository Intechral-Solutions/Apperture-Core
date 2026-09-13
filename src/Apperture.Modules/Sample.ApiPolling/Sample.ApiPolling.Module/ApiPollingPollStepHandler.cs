using Apperture.Core.Application.Execution;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

/// <summary>
/// Loads the embedded snapshot via the fake API client and opens a process-local Running sync batch.
/// Pages through the full matching set so Compare can detect missing/deleted keys.
/// Retry mode loads exactly one record by sequence.
/// </summary>
public sealed class ApiPollingPollStepHandler : IStepHandler
{
    private readonly IApiPollingStateStore _store;
    private readonly ISampleDirectoryApiClient _client;

    public ApiPollingPollStepHandler(IApiPollingStateStore store, ISampleDirectoryApiClient client)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var snapshotId = ApiPollingParameterReader.GetString(parameters, "snapshotId")?.Trim();
        if (string.IsNullOrWhiteSpace(snapshotId) || !ApiPollingSnapshotIds.IsKnown(snapshotId))
        {
            throw OperatorExecutionResult.FailSummaryOnly(
                "Poll requires a known snapshotId (Validate should have rejected unknown ids).");
        }

        var pageSize = ApiPollingParameterReader.ResolvePageSize(parameters);
        var includeDeleted = ApiPollingParameterReader.ResolveIncludeDeleted(parameters);
        var resetState = ApiPollingParameterReader.ResolveResetState(parameters);
        var retryMode = ApiPollingParameterReader.ResolveRetryMode(parameters);

        var integrationId = context.Signals.Correlation.IntegrationId ?? context.IntegrationId;
        var scopeKey = _store.ResolveScopeKey(integrationId);
        if (resetState && !retryMode)
            _store.ResetScope(scopeKey);

        List<DirectoryAccountRecord> all;
        int totalMatching;
        try
        {
            (all, totalMatching) = await LoadAllPagesAsync(
                snapshotId,
                pageSize,
                includeDeleted,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            throw OperatorExecutionResult.FailSummaryOnly(ex.Message);
        }

        if (retryMode)
        {
            var sequence = ApiPollingParameterReader.GetInt(
                parameters,
                ApiPollingOperationItemRetryHandler.RetrySequenceParameter);
            if (sequence is null or < 1 || sequence.Value > all.Count)
            {
                throw OperatorExecutionResult.FailSummaryOnly(
                    $"ApiPolling retry could not find sequence {sequence} in snapshot '{snapshotId}'.");
            }

            all = [all[sequence.Value - 1]];
            totalMatching = 1;
        }

        var runId = context.Signals.Correlation.RunId ?? context.RunId;
        var correlationId = context.Signals.Correlation.CorrelationId ?? context.CorrelationId;

        var batch = _store.BeginSync(new SyncBatchCreateRequest(
            SnapshotId: snapshotId,
            ScopeKey: scopeKey,
            RunId: runId,
            CorrelationId: correlationId,
            IntegrationId: integrationId,
            ResetState: resetState && !retryMode));

        _store.SetPolledRecords(batch.SyncBatchId, all);

        return OperatorExecutionResult.SuccessSummaryOnly(new
        {
            step = "Poll",
            snapshotId,
            syncBatchId = batch.SyncBatchId,
            scopeKey,
            resetState = batch.ResetState,
            retryMode,
            includeDeleted,
            pageSize,
            recordCount = all.Count,
            totalMatching,
        });
    }

    private async Task<(List<DirectoryAccountRecord> Records, int TotalMatching)> LoadAllPagesAsync(
        string snapshotId,
        int pageSize,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var all = new List<DirectoryAccountRecord>();
        var offset = 0;
        var totalMatching = 0;
        const int maxPages = 100;

        for (var page = 0; page < maxPages; page++)
        {
            var result = await _client.GetSnapshotAsync(
                snapshotId,
                new ApiPollQuery(pageSize, offset, includeDeleted),
                cancellationToken).ConfigureAwait(false);

            totalMatching = result.TotalMatching;
            if (result.Records.Count == 0)
                break;

            all.AddRange(result.Records);
            offset += result.Records.Count;
            if (offset >= totalMatching)
                break;
        }

        return (all, totalMatching);
    }
}
