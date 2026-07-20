using System.Text.Json;
using Apperture.Core.Application.OperationItems;
using Apperture.RuntimePlane.Abstractions;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

/// <summary>
/// SDK-5C — ApiPolling failed-record retry handler.
/// Reloads one embedded snapshot record by snapshotId + sequence; does not store raw JSON payloads.
/// </summary>
public sealed class ApiPollingOperationItemRetryHandler : IOperationItemRetryHandler
{
    public const string RetryModeParameter = "retryMode";
    public const string RetrySequenceParameter = "retrySequence";
    public const string ParentOperationItemIdParameter = "parentOperationItemId";
    public const string RetryAttemptIdParameter = "retryAttemptId";

    private readonly ISampleDirectoryApiClient _client;

    public ApiPollingOperationItemRetryHandler(ISampleDirectoryApiClient? client = null) =>
        _client = client ?? new EmbeddedSampleDirectoryApiClient();

    public string ModuleId => ApiPollingModule.ModuleIdValue;

    public string OperationKind => ApiPollingOperationItemization.OperationKind;

    public bool Supports(OperationItem item, OperationGroup group) =>
        string.Equals(group.ModuleId, ModuleId, StringComparison.Ordinal)
        && string.Equals(group.OperationKind, OperationKind, StringComparison.Ordinal);

    public OperationItemRetryEligibility Evaluate(OperationItem item, OperationGroup group)
    {
        _ = group;
        if (!string.Equals(item.Status, OperationItemStatuses.Failed, StringComparison.Ordinal))
        {
            return new OperationItemRetryEligibility(
                false,
                OperationItemRetryErrorCodes.NotFailed,
                $"Item status '{item.Status}' is not Failed.");
        }

        if (!item.IsRetryable)
        {
            return new OperationItemRetryEligibility(
                false,
                OperationItemRetryErrorCodes.NotRetryable,
                "Item is not marked retryable.");
        }

        if (!TryReadMetadata(item.MetadataJson, out var snapshotId, out var sequence, out var includeDeleted, out var metaError))
        {
            return new OperationItemRetryEligibility(
                false,
                OperationItemRetryErrorCodes.MissingRetryMetadata,
                metaError);
        }

        if (!ApiPollingSnapshotIds.IsKnown(snapshotId))
        {
            return new OperationItemRetryEligibility(
                false,
                OperationItemRetryErrorCodes.MissingRetryMetadata,
                $"Unknown snapshotId '{snapshotId}' in item metadata.");
        }

        if (!TryLoadRecord(snapshotId, sequence, includeDeleted, out _, out var loadError))
        {
            return new OperationItemRetryEligibility(
                false,
                OperationItemRetryErrorCodes.RecordNotFound,
                loadError);
        }

        return new OperationItemRetryEligibility(true);
    }

    public OperationItemRetryStartPlan BuildStartPlan(OperationItemRetryBuildContext context)
    {
        if (!TryReadMetadata(
                context.Item.MetadataJson,
                out var snapshotId,
                out var sequence,
                out var includeDeleted,
                out var metaError))
        {
            throw new OperationItemizationException(metaError);
        }

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["snapshotId"] = snapshotId,
            [RetryModeParameter] = true,
            [RetrySequenceParameter] = sequence,
            [ParentOperationItemIdParameter] = context.Item.OperationItemId,
            [RetryAttemptIdParameter] = context.RetryAttemptId,
            ["includeDeleted"] = includeDeleted,
            // Single-record retry — do not inherit batch fail/strict from the original sync.
            ["failOnRecordError"] = false,
            ["strictMode"] = false,
            ["resetState"] = false,
        };

        return new OperationItemRetryStartPlan(
            ApiPollingModule.DefinitionId,
            parameters,
            context.Group.IntegrationId,
            context.Group.ProcedureId);
    }

    internal bool TryLoadRecord(
        string snapshotId,
        int sequence,
        bool includeDeleted,
        out DirectoryAccountRecord? record,
        out string error)
    {
        record = null;
        error = "";

        try
        {
            var records = LoadAllRecords(snapshotId, includeDeleted);
            if (sequence < 1 || sequence > records.Count)
            {
                error = $"Sequence {sequence} was not found in snapshot '{snapshotId}' (record count={records.Count}).";
                return false;
            }

            record = records[sequence - 1];
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private List<DirectoryAccountRecord> LoadAllRecords(string snapshotId, bool includeDeleted)
    {
        var all = new List<DirectoryAccountRecord>();
        var offset = 0;
        const int pageSize = 50;
        const int maxPages = 100;

        for (var page = 0; page < maxPages; page++)
        {
            var result = _client.GetSnapshotAsync(
                    snapshotId,
                    new ApiPollQuery(pageSize, offset, includeDeleted))
                .GetAwaiter()
                .GetResult();

            if (result.Records.Count == 0)
                break;

            all.AddRange(result.Records);
            offset += result.Records.Count;
            if (offset >= result.TotalMatching)
                break;
        }

        return all;
    }

    internal static bool TryReadMetadata(
        string? metadataJson,
        out string snapshotId,
        out int sequence,
        out bool includeDeleted,
        out string error)
    {
        snapshotId = "";
        sequence = 0;
        includeDeleted = ApiPollingSettingDefaults.IncludeDeleted;
        error = "";

        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            error = "Operation item metadata is missing (expected snapshotId and sequence). "
                + "Items written before SDK-5C may lack sequence and cannot be retried.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("snapshotId", out var snapshotEl)
                || snapshotEl.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(snapshotEl.GetString()))
            {
                error = "Operation item metadata is missing snapshotId.";
                return false;
            }

            if (!root.TryGetProperty("sequence", out var seqEl)
                || seqEl.ValueKind != JsonValueKind.Number
                || !seqEl.TryGetInt32(out var seq)
                || seq < 1)
            {
                error = "Operation item metadata is missing a valid sequence. "
                    + "Items written before SDK-5C may lack sequence and cannot be retried.";
                return false;
            }

            if (root.TryGetProperty("includeDeleted", out var deletedEl))
            {
                includeDeleted = deletedEl.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => includeDeleted,
                };
            }

            snapshotId = snapshotEl.GetString()!.Trim();
            sequence = seq;
            return true;
        }
        catch (JsonException)
        {
            error = "Operation item metadata JSON is invalid.";
            return false;
        }
    }
}
