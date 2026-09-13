namespace Apperture.Core.Application.RuntimeHealth;

/// <summary>SWM-6: schedule assignment / runtime-health plane default shared by Management API.</summary>
public static class ScheduleRuntimePlaneDefaults
{
    /// <summary>Default plane used when create/assign omits RuntimePlaneId (matches SchedulesController).</summary>
    public const string ExpectedManagementPlaneId = "local";
}

/// <summary>SWM-6: operator-facing scheduler loop classification.</summary>
public static class SchedulerLoopStatus
{
    public const string Healthy = "Healthy";
    public const string Warning = "Warning";
    public const string Unknown = "Unknown";
}

/// <summary>SWM-6: alignment of observed worker plane vs Management expected plane.</summary>
public static class SchedulerPlaneAlignment
{
    public const string Aligned = "Aligned";
    public const string Mismatched = "Mismatched";
    public const string Unknown = "Unknown";
}

/// <summary>Builds scheduler status classification from a heartbeat (pure; unit-testable).</summary>
public static class SchedulerStatusClassifier
{
    public const int MinStaleSeconds = 60;
    public const int MaxErrorSummaryLength = 512;

    public static TimeSpan ComputeStaleThreshold(int pollIntervalSeconds)
    {
        var poll = Math.Max(1, pollIntervalSeconds);
        return TimeSpan.FromSeconds(Math.Max(MinStaleSeconds, poll * 2));
    }

    public static bool IsStale(SchedulerDispatchHeartbeatSnapshot? heartbeat, DateTimeOffset asOfUtc)
    {
        if (heartbeat is null)
            return false;

        var threshold = ComputeStaleThreshold(heartbeat.PollIntervalSeconds);
        var anchor = heartbeat.UpdatedUtc;
        return asOfUtc - anchor > threshold;
    }

    public static string Classify(SchedulerDispatchHeartbeatSnapshot? heartbeat, DateTimeOffset asOfUtc)
    {
        if (heartbeat is null)
            return SchedulerLoopStatus.Unknown;

        if (!string.IsNullOrWhiteSpace(heartbeat.LastErrorSummary) || IsStale(heartbeat, asOfUtc))
            return SchedulerLoopStatus.Warning;

        return SchedulerLoopStatus.Healthy;
    }

    public static string ClassifyAlignment(string expectedPlaneId, string? observedPlaneId)
    {
        if (string.IsNullOrWhiteSpace(observedPlaneId))
            return SchedulerPlaneAlignment.Unknown;

        return string.Equals(expectedPlaneId.Trim(), observedPlaneId.Trim(), StringComparison.OrdinalIgnoreCase)
            ? SchedulerPlaneAlignment.Aligned
            : SchedulerPlaneAlignment.Mismatched;
    }

    public static string? BuildGuidance(string status, bool isStale, string alignment, string? lastError)
    {
        if (string.Equals(status, SchedulerLoopStatus.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            return "No scheduler heartbeat for the expected plane. WorkerHost is likely not running, never started a cycle, "
                   + "or cannot write heartbeats to the shared database. "
                   + "Manual recovery run-cycle does not write this heartbeat.";
        }

        if (!string.IsNullOrWhiteSpace(lastError))
        {
            return "Last scheduler cycle reported an error. Check WorkerHost logs for Plane/HostId context. "
                   + "Dispatch may still retry on the next poll.";
        }

        if (isStale)
        {
            return "Scheduler heartbeat is stale. The WorkerHost timer may have stopped, crashed, or lost database connectivity "
                   + "(heartbeat writes are best-effort — a write failure leaves the previous row aging).";
        }

        if (string.Equals(alignment, SchedulerPlaneAlignment.Mismatched, StringComparison.OrdinalIgnoreCase))
        {
            return "Observed dispatch plane does not match Management expected plane. Assignments may not be evaluated.";
        }

        return null;
    }

    /// <summary>SWM-7: builds the worker runtime projection from an optional heartbeat snapshot.</summary>
    public static WorkerRuntimeStatus BuildWorkerRuntime(
        string expectedPlaneId,
        SchedulerDispatchHeartbeatSnapshot? snap,
        DateTimeOffset asOfUtc,
        DateTimeOffset? processStartedUtc = null,
        string? applicationVersion = null,
        string? environmentName = null,
        string inventoryModel = WorkerRuntimeInventoryModel.SingleHostObserved)
    {
        var status = Classify(snap, asOfUtc);
        var isStale = IsStale(snap, asOfUtc);
        var alignment = ClassifyAlignment(expectedPlaneId, snap?.RuntimePlaneId);
        var guidance = BuildGuidance(status, isStale, alignment, snap?.LastErrorSummary);

        return new WorkerRuntimeStatus(
            InventoryModel: inventoryModel,
            Status: status,
            HostId: snap?.HostId,
            RuntimePlaneId: snap?.RuntimePlaneId,
            LastSeenUtc: snap?.UpdatedUtc,
            Alignment: alignment,
            SchedulerStatus: status,
            ProcessStartedUtc: processStartedUtc,
            ApplicationVersion: applicationVersion,
            EnvironmentName: environmentName,
            Guidance: guidance);
    }

    /// <summary>SWM-11: maps one heartbeat row to an observed-worker projection.</summary>
    public static ObservedWorkerStatus BuildObservedWorker(
        string expectedPlaneId,
        SchedulerDispatchHeartbeatSnapshot snap,
        DateTimeOffset asOfUtc,
        DateTimeOffset? processStartedUtc = null,
        string? applicationVersion = null,
        string? environmentName = null)
    {
        var status = Classify(snap, asOfUtc);
        var isStale = IsStale(snap, asOfUtc);
        var alignment = ClassifyAlignment(expectedPlaneId, snap.RuntimePlaneId);
        var guidance = BuildGuidance(status, isStale, alignment, snap.LastErrorSummary);

        return new ObservedWorkerStatus(
            HostId: snap.HostId,
            RuntimePlaneId: snap.RuntimePlaneId,
            Status: status,
            IsStale: isStale,
            LastSeenUtc: snap.UpdatedUtc,
            ProcessStartedUtc: processStartedUtc,
            ApplicationVersion: applicationVersion,
            EnvironmentName: environmentName,
            PollIntervalSeconds: snap.PollIntervalSeconds,
            LastCycleCompletedUtc: snap.LastCycleCompletedUtc,
            LastErrorSummary: snap.LastErrorSummary,
            Alignment: alignment,
            Guidance: guidance);
    }

    /// <summary>SWM-11: observation-level guidance (observe-only; lifecycle externally managed).</summary>
    public static string? BuildObservationSummaryGuidance(
        int observedCount,
        int staleCount,
        bool hasPlaneMismatch,
        bool hasDuplicateHostIdentityHint)
    {
        if (observedCount == 0)
        {
            return "No workers observed for the expected plane. Confirm WorkerHost is running and can write heartbeats "
                   + "(lifecycle is externally managed — Aspire AppHost, Manage.ps1, or host adapters).";
        }

        if (observedCount == 1 && staleCount == 0 && !hasPlaneMismatch)
            return null;

        var parts = new List<string>();

        if (observedCount > 1)
        {
            parts.Add(
                $"{observedCount} worker hosts observed on this plane. Primary scheduler status uses the most recently updated heartbeat. "
                + "Multiple hosts on one plane can be valid (Epic36 claim leases); confirm unintended duplicates are not from mixing Aspire and Manage.ps1.");
        }

        if (staleCount > 0)
            parts.Add($"{staleCount} observed worker heartbeat(s) are stale — timer may have stopped or lost DB connectivity.");

        if (hasPlaneMismatch)
            parts.Add("At least one observed worker reports a plane that does not match Management expected plane.");

        if (hasDuplicateHostIdentityHint)
        {
            parts.Add(
                "Same HostId reused across distinct process starts can look like one host; confirm APERTURE_RUNTIME_ID / SchedulerHostId uniqueness.");
        }

        parts.Add("WorkerHost lifecycle remains externally managed — Management does not start, stop, or restart workers.");

        return string.Join(" ", parts);
    }

    public static string? SanitizeErrorSummary(Exception? exception, int maxLength = MaxErrorSummaryLength)
    {
        if (exception is null)
            return null;

        var raw = $"{exception.GetType().Name}: {exception.Message}";
        return SanitizeErrorSummary(raw, maxLength);
    }

    public static string? SanitizeErrorSummary(string? message, int maxLength = MaxErrorSummaryLength)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var cleaned = message.Trim()
            .Replace('\r', ' ')
            .Replace('\n', ' ');

        // Strip common secret-bearing fragments if they appeared in an exception message.
        cleaned = System.Text.RegularExpressions.Regex.Replace(
            cleaned,
            @"(Password|Pwd|AccountKey|SharedAccessSignature|Secret|ApiKey)\s*=\s*[^;\s]+",
            "$1=***",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (cleaned.Length > maxLength)
            cleaned = cleaned[..maxLength];

        return cleaned;
    }
}

/// <summary>Snapshot used by classifier (avoids persistence dependency in pure helpers).</summary>
public sealed record SchedulerDispatchHeartbeatSnapshot(
    string RuntimePlaneId,
    string HostId,
    int PollIntervalSeconds,
    DateTimeOffset LastCycleStartedUtc,
    DateTimeOffset? LastCycleCompletedUtc,
    DateTimeOffset? LastSuccessfulCycleUtc,
    string? LastErrorSummary,
    int AssignmentCount,
    int DueCount,
    int StaleCount,
    int CandidateCount,
    int ClaimedCount,
    int DispatchedCount,
    int SkippedCount,
    DateTimeOffset UpdatedUtc);
