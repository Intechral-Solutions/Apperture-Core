using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

public static class ApiPollingParameterReader
{
    public static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => value.ToString(),
        };
    }

    public static bool? GetBool(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            bool b => b,
            JsonElement je when je.ValueKind == JsonValueKind.True => true,
            JsonElement je when je.ValueKind == JsonValueKind.False => false,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }

    public static int? GetInt(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            int i => i,
            long l => checked((int)l),
            JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var n) => n,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    public static bool ResolveStrictMode(IReadOnlyDictionary<string, object?> parameters) =>
        GetBool(parameters, "strictMode") ?? ApiPollingSettingDefaults.StrictModeDefault;

    public static bool ResolveFailOnRecordError(IReadOnlyDictionary<string, object?> parameters) =>
        GetBool(parameters, "failOnRecordError") ?? ApiPollingSettingDefaults.FailOnRecordError;

    public static bool ResolveIncludeDeleted(IReadOnlyDictionary<string, object?> parameters) =>
        GetBool(parameters, "includeDeleted") ?? ApiPollingSettingDefaults.IncludeDeleted;

    public static bool ResolveResetState(IReadOnlyDictionary<string, object?> parameters) =>
        GetBool(parameters, "resetState") ?? false;

    public static bool ResolveRetryMode(IReadOnlyDictionary<string, object?> parameters) =>
        GetBool(parameters, ApiPollingOperationItemRetryHandler.RetryModeParameter) == true;

    public static int ResolvePageSize(IReadOnlyDictionary<string, object?> parameters)
    {
        var size = GetInt(parameters, "pageSize") ?? ApiPollingSettingDefaults.PageSize;
        return size <= 0 ? ApiPollingSettingDefaults.PageSize : size;
    }

    public static int ResolveMaxRecords(IReadOnlyDictionary<string, object?> parameters)
    {
        var max = GetInt(parameters, "maxRecordsPerSync") ?? ApiPollingSettingDefaults.MaxRecordsPerSync;
        return max <= 0 ? ApiPollingSettingDefaults.MaxRecordsPerSync : max;
    }
}

public static class ApiPollingClassifier
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Active",
        "Inactive",
        "Pending",
    };

    public static IReadOnlyList<RecordClassification> Classify(
        string syncBatchId,
        IReadOnlyList<DirectoryAccountRecord> polledRecords,
        IReadOnlyDictionary<string, SeenAccountFingerprint> lastSeen,
        bool includeDeleted,
        int maxRecords,
        bool failOnRecordError,
        DateTimeOffset nowUtc)
    {
        var results = new List<RecordClassification>();
        var seenInPoll = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stopAfterFailure = false;
        var processedCount = 0;
        var sequence = 0;

        foreach (var record in polledRecords)
        {
            sequence++;
            RecordClassification classification;

            if (stopAfterFailure)
            {
                classification = Skipped(
                    syncBatchId,
                    sequence,
                    record,
                    ApiPollingRecordErrorCodes.SkippedFailOnRecordError,
                    "Skipped because failOnRecordError stopped processing after an earlier failure.",
                    nowUtc);
            }
            else if (processedCount >= maxRecords)
            {
                classification = Skipped(
                    syncBatchId,
                    sequence,
                    record,
                    ApiPollingRecordErrorCodes.SkippedMaxRecords,
                    $"Skipped because maxRecordsPerSync={maxRecords}.",
                    nowUtc);
            }
            else
            {
                classification = ClassifyRecord(syncBatchId, sequence, record, lastSeen, seenInPoll, nowUtc);
                processedCount++;
                if (classification.ChangeKind == ApiPollingChangeKinds.Failed && failOnRecordError)
                    stopAfterFailure = true;
            }

            results.Add(classification);
        }

        if (includeDeleted)
        {
            foreach (var prior in lastSeen.Values.OrderBy(v => v.ExternalId, StringComparer.OrdinalIgnoreCase))
            {
                if (seenInPoll.Contains(prior.ExternalId))
                    continue;

                sequence++;
                if (stopAfterFailure)
                {
                    results.Add(new RecordClassification(
                        syncBatchId,
                        sequence,
                        prior.ExternalId,
                        prior.DisplayName,
                        ApiPollingChangeKinds.Skipped,
                        "Skipped",
                        ApiPollingRecordErrorCodes.SkippedFailOnRecordError,
                        "Skipped because failOnRecordError stopped processing after an earlier failure.",
                        $"changeKind=skipped; id={prior.ExternalId}",
                        prior.Version,
                        prior.Status,
                        prior.Tier,
                        null,
                        nowUtc));
                    continue;
                }

                if (processedCount >= maxRecords)
                {
                    results.Add(new RecordClassification(
                        syncBatchId,
                        sequence,
                        prior.ExternalId,
                        prior.DisplayName,
                        ApiPollingChangeKinds.Skipped,
                        "Skipped",
                        ApiPollingRecordErrorCodes.SkippedMaxRecords,
                        $"Skipped because maxRecordsPerSync={maxRecords}.",
                        $"changeKind=skipped; id={prior.ExternalId}",
                        prior.Version,
                        prior.Status,
                        prior.Tier,
                        null,
                        nowUtc));
                    continue;
                }

                results.Add(new RecordClassification(
                    syncBatchId,
                    sequence,
                    prior.ExternalId,
                    prior.DisplayName,
                    ApiPollingChangeKinds.Deleted,
                    "Succeeded",
                    null,
                    null,
                    $"changeKind=deleted; id={prior.ExternalId}; reason=missing_from_snapshot",
                    prior.Version,
                    prior.Status,
                    prior.Tier,
                    ["presence"],
                    nowUtc));
                processedCount++;
            }
        }

        return results;
    }

    private static RecordClassification ClassifyRecord(
        string syncBatchId,
        int sequence,
        DirectoryAccountRecord record,
        IReadOnlyDictionary<string, SeenAccountFingerprint> lastSeen,
        HashSet<string> seenInPoll,
        DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(record.ExternalId))
        {
            return Failed(
                syncBatchId,
                sequence,
                null,
                record.DisplayName,
                ApiPollingRecordErrorCodes.MissingExternalId,
                "externalId is required.",
                record,
                nowUtc);
        }

        var externalId = record.ExternalId.Trim();
        if (!seenInPoll.Add(externalId))
        {
            return Failed(
                syncBatchId,
                sequence,
                externalId,
                record.DisplayName,
                ApiPollingRecordErrorCodes.DuplicateExternalId,
                $"Duplicate externalId '{externalId}'.",
                record,
                nowUtc);
        }

        if (!IsValidEmail(record.Email))
        {
            return Failed(
                syncBatchId,
                sequence,
                externalId,
                record.DisplayName,
                ApiPollingRecordErrorCodes.InvalidEmail,
                $"Invalid email '{record.Email}'.",
                record,
                nowUtc);
        }

        if (string.IsNullOrWhiteSpace(record.Status) || !AllowedStatuses.Contains(record.Status))
        {
            return Failed(
                syncBatchId,
                sequence,
                externalId,
                record.DisplayName,
                ApiPollingRecordErrorCodes.InvalidStatus,
                $"Invalid status '{record.Status}'. Allowed: Active, Inactive, Pending.",
                record,
                nowUtc);
        }

        if (!DateTimeOffset.TryParse(record.UpdatedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)
            && !DateTimeOffset.TryParse(record.UpdatedAtUtc, out _))
        {
            return Failed(
                syncBatchId,
                sequence,
                externalId,
                record.DisplayName,
                ApiPollingRecordErrorCodes.InvalidUpdatedAt,
                $"Invalid updatedAtUtc '{record.UpdatedAtUtc}'.",
                record,
                nowUtc);
        }

        if (record.IsDeleted)
        {
            return new RecordClassification(
                syncBatchId,
                sequence,
                externalId,
                record.DisplayName,
                ApiPollingChangeKinds.Deleted,
                "Succeeded",
                null,
                null,
                $"changeKind=deleted; id={externalId}; reason=isDeleted",
                record.Version,
                record.Status,
                record.Tier,
                ["isDeleted"],
                nowUtc);
        }

        if (!lastSeen.TryGetValue(externalId, out var prior))
        {
            return new RecordClassification(
                syncBatchId,
                sequence,
                externalId,
                record.DisplayName,
                ApiPollingChangeKinds.New,
                "Succeeded",
                null,
                null,
                $"changeKind=new; id={externalId}",
                record.Version,
                record.Status,
                record.Tier,
                null,
                nowUtc);
        }

        var changedFields = DiffFields(prior, record);
        if (changedFields.Count == 0)
        {
            return new RecordClassification(
                syncBatchId,
                sequence,
                externalId,
                record.DisplayName,
                ApiPollingChangeKinds.Unchanged,
                "Succeeded",
                null,
                null,
                $"changeKind=unchanged; id={externalId}",
                record.Version,
                record.Status,
                record.Tier,
                null,
                nowUtc);
        }

        return new RecordClassification(
            syncBatchId,
            sequence,
            externalId,
            record.DisplayName,
            ApiPollingChangeKinds.Updated,
            "Succeeded",
            null,
            null,
            $"changeKind=updated; id={externalId}; fields={string.Join(",", changedFields)}",
            record.Version,
            record.Status,
            record.Tier,
            changedFields,
            nowUtc);
    }

    public static SyncBatchStatus ResolveBatchStatus(int succeeded, int failed, int skipped, bool strictMode)
    {
        _ = skipped;
        if (failed > 0 && (strictMode || succeeded == 0))
            return SyncBatchStatus.Failed;

        if (failed > 0 && succeeded > 0)
            return SyncBatchStatus.CompletedWithErrors;

        return SyncBatchStatus.Succeeded;
    }

    public static string SignalForBatch(
        SyncBatchStatus status,
        int newCount,
        int updatedCount,
        int deletedCount) =>
        status switch
        {
            SyncBatchStatus.CompletedWithErrors => "sync.completed.with.errors",
            SyncBatchStatus.Failed => "sync.failed",
            SyncBatchStatus.Succeeded when newCount + updatedCount + deletedCount > 0 => "sync.completed.with.changes",
            SyncBatchStatus.Succeeded => "sync.completed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Running batches cannot emit."),
        };

    public static SeenAccountFingerprint ToFingerprint(DirectoryAccountRecord record, DateTimeOffset nowUtc) =>
        new(
            record.ExternalId!.Trim(),
            record.DisplayName,
            record.Email,
            record.Status,
            record.Tier,
            record.UpdatedAtUtc,
            record.Version,
            record.IsDeleted,
            nowUtc);

    private static List<string> DiffFields(SeenAccountFingerprint prior, DirectoryAccountRecord current)
    {
        // Prefer version when both present.
        if (prior.Version is not null && current.Version is not null && prior.Version != current.Version)
        {
            var fields = new List<string> { "version" };
            if (!string.Equals(prior.DisplayName, current.DisplayName, StringComparison.Ordinal))
                fields.Add("displayName");
            if (!string.Equals(prior.Email, current.Email, StringComparison.OrdinalIgnoreCase))
                fields.Add("email");
            if (!string.Equals(prior.Status, current.Status, StringComparison.OrdinalIgnoreCase))
                fields.Add("status");
            if (!string.Equals(prior.Tier, current.Tier, StringComparison.OrdinalIgnoreCase))
                fields.Add("tier");
            if (!string.Equals(prior.UpdatedAtUtc, current.UpdatedAtUtc, StringComparison.Ordinal))
                fields.Add("updatedAtUtc");
            if (prior.IsDeleted != current.IsDeleted)
                fields.Add("isDeleted");
            return fields;
        }

        var changed = new List<string>();
        if (!string.Equals(prior.DisplayName, current.DisplayName, StringComparison.Ordinal))
            changed.Add("displayName");
        if (!string.Equals(prior.Email, current.Email, StringComparison.OrdinalIgnoreCase))
            changed.Add("email");
        if (!string.Equals(prior.Status, current.Status, StringComparison.OrdinalIgnoreCase))
            changed.Add("status");
        if (!string.Equals(prior.Tier, current.Tier, StringComparison.OrdinalIgnoreCase))
            changed.Add("tier");
        if (!string.Equals(prior.UpdatedAtUtc, current.UpdatedAtUtc, StringComparison.Ordinal))
            changed.Add("updatedAtUtc");
        if (prior.Version != current.Version)
            changed.Add("version");
        if (prior.IsDeleted != current.IsDeleted)
            changed.Add("isDeleted");
        return changed;
    }

    private static RecordClassification Failed(
        string syncBatchId,
        int sequence,
        string? externalId,
        string? displayName,
        string code,
        string message,
        DirectoryAccountRecord record,
        DateTimeOffset nowUtc) =>
        new(
            syncBatchId,
            sequence,
            externalId,
            displayName,
            ApiPollingChangeKinds.Failed,
            "Failed",
            code,
            message,
            $"changeKind=failed; id={externalId ?? "(no-id)"}; code={code}",
            record.Version,
            record.Status,
            record.Tier,
            null,
            nowUtc);

    private static RecordClassification Skipped(
        string syncBatchId,
        int sequence,
        DirectoryAccountRecord record,
        string code,
        string message,
        DateTimeOffset nowUtc) =>
        new(
            syncBatchId,
            sequence,
            string.IsNullOrWhiteSpace(record.ExternalId) ? null : record.ExternalId.Trim(),
            record.DisplayName,
            ApiPollingChangeKinds.Skipped,
            "Skipped",
            code,
            message,
            $"changeKind=skipped; id={record.ExternalId ?? "(no-id)"}; code={code}",
            record.Version,
            record.Status,
            record.Tier,
            null,
            nowUtc);

    private static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            _ = new MailAddress(email);
            return email.Contains('@', StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
