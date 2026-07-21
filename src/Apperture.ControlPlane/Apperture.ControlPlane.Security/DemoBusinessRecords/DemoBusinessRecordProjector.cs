using System.Text.Json;
using Apperture.Core.Application.OperationItems;

namespace Apperture.ControlPlane.Security.DemoBusinessRecords;

/// <summary>
/// Demo/sample read-only business record projection (BDS-3).
/// Not a platform-wide <c>BusinessRecord</c> entity model.
/// </summary>
public sealed record DemoBusinessRecordProjection(
    string RecordType,
    string BusinessKey,
    string? DisplayName,
    string? PrimaryEmail,
    string? PrimaryStatus,
    int SourceCount,
    bool HasConflict,
    int ConflictCount,
    DateTimeOffset? LastObservedAtUtc,
    IReadOnlyList<DemoBusinessRecordSourceObservation> Sources,
    IReadOnlyList<DemoBusinessRecordConflict> Conflicts);

public sealed record DemoBusinessRecordSourceObservation(
    string SourceKind,
    string ModuleId,
    string? IntegrationId,
    string? OperationKind,
    string OperationItemId,
    string OperationGroupId,
    string RunId,
    string BusinessKey,
    string? DisplayName,
    string? Email,
    string? Status,
    string? ChangeKind,
    DateTimeOffset ObservedAtUtc,
    string ProvenanceLabel);

public sealed record DemoBusinessRecordConflict(
    string Field,
    IReadOnlyDictionary<string, string> ValuesBySource,
    string Message);

public sealed record DemoBusinessRecordListResult(
    IReadOnlyList<DemoBusinessRecordProjection> Items,
    int TotalCount,
    int Skip,
    int Take);

public sealed record DemoBusinessRecordListQuery(
    string? Q = null,
    string? SourceKind = null,
    bool? HasConflict = null,
    int Skip = 0,
    int Take = 50);

/// <summary>Raw observation input for the pure projector (tests + service).</summary>
public sealed record DemoBusinessRecordObservationInput(
    string RecordType,
    string BusinessKey,
    string ModuleId,
    string? IntegrationId,
    string? OperationKind,
    string OperationItemId,
    string OperationGroupId,
    string RunId,
    string? DisplayName,
    string? Email,
    string? Status,
    string? ChangeKind,
    DateTimeOffset ObservedAtUtc);

public static class DemoBusinessRecordSourceKinds
{
    public const string FileImport = "file_import";
    public const string ApiPolling = "api_polling";
    public const string Unknown = "unknown";

    public static string FromModuleId(string? moduleId)
    {
        if (string.Equals(moduleId, "Sample.FileImport", StringComparison.OrdinalIgnoreCase))
            return FileImport;
        if (string.Equals(moduleId, "Sample.ApiPolling", StringComparison.OrdinalIgnoreCase))
            return ApiPolling;
        return Unknown;
    }
}

/// <summary>Pure read-time projection builder (no I/O).</summary>
public static class DemoBusinessRecordProjector
{
    public static IReadOnlyList<DemoBusinessRecordProjection> Project(
        IEnumerable<DemoBusinessRecordObservationInput> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var byKey = new Dictionary<string, List<DemoBusinessRecordObservationInput>>(StringComparer.OrdinalIgnoreCase);
        foreach (var obs in observations)
        {
            if (string.IsNullOrWhiteSpace(obs.RecordType)
                || !string.Equals(obs.RecordType, OperationItemBusinessMetadataKeys.CustomerRecordType, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(obs.BusinessKey))
                continue;

            var key = obs.BusinessKey.Trim();
            if (!byKey.TryGetValue(key, out var list))
            {
                list = [];
                byKey[key] = list;
            }

            list.Add(obs with
            {
                BusinessKey = key,
                RecordType = OperationItemBusinessMetadataKeys.CustomerRecordType,
            });
        }

        return byKey
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => ProjectOne(kv.Key, kv.Value)!)
            .ToArray();
    }

    public static DemoBusinessRecordProjection? ProjectOne(
        string businessKey,
        IReadOnlyList<DemoBusinessRecordObservationInput> observations)
    {
        if (string.IsNullOrWhiteSpace(businessKey) || observations.Count == 0)
            return null;

        // Latest observation per module + integration.
        var latestBySource = observations
            .GroupBy(
                o => $"{o.ModuleId}\u001f{o.IntegrationId ?? ""}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.ObservedAtUtc).First())
            .OrderBy(o => o.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.IntegrationId ?? "", StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sources = latestBySource.Select(ToSource).ToArray();
        var conflicts = DetectConflicts(sources);
        var primary = ChoosePrimary(sources);

        return new DemoBusinessRecordProjection(
            RecordType: OperationItemBusinessMetadataKeys.CustomerRecordType,
            BusinessKey: businessKey.Trim(),
            DisplayName: primary.DisplayName,
            PrimaryEmail: primary.Email,
            PrimaryStatus: primary.Status,
            SourceCount: sources.Length,
            HasConflict: conflicts.Count > 0,
            ConflictCount: conflicts.Count,
            LastObservedAtUtc: sources.Max(s => s.ObservedAtUtc),
            Sources: sources,
            Conflicts: conflicts);
    }

    public static DemoBusinessRecordListResult ApplyListQuery(
        IReadOnlyList<DemoBusinessRecordProjection> all,
        DemoBusinessRecordListQuery query)
    {
        IEnumerable<DemoBusinessRecordProjection> filtered = all;

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim();
            filtered = filtered.Where(r =>
                r.BusinessKey.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (r.DisplayName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.PrimaryEmail?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!string.IsNullOrWhiteSpace(query.SourceKind))
        {
            var kind = query.SourceKind.Trim();
            filtered = filtered.Where(r =>
                r.Sources.Any(s => string.Equals(s.SourceKind, kind, StringComparison.OrdinalIgnoreCase)));
        }

        if (query.HasConflict is bool wantConflict)
            filtered = filtered.Where(r => r.HasConflict == wantConflict);

        var materialized = filtered
            .OrderBy(r => r.BusinessKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var skip = Math.Max(0, query.Skip);
        var take = query.Take <= 0 ? 50 : Math.Min(query.Take, 200);
        var page = materialized.Skip(skip).Take(take).ToArray();
        return new DemoBusinessRecordListResult(page, materialized.Length, skip, take);
    }

    public static DemoBusinessRecordObservationInput? TryParseItemObservation(
        string operationItemId,
        string operationGroupId,
        string runId,
        string moduleId,
        string? integrationId,
        string? operationKind,
        string? subjectKey,
        string? displayLabel,
        string? metadataJson,
        string? changeSummaryJson,
        DateTimeOffset? completedAtUtc,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty(OperationItemBusinessMetadataKeys.RecordType, out var rtEl))
                return null;
            var recordType = rtEl.GetString();
            if (!string.Equals(recordType, OperationItemBusinessMetadataKeys.CustomerRecordType, StringComparison.OrdinalIgnoreCase))
                return null;

            var businessKey = ReadString(root, OperationItemBusinessMetadataKeys.BusinessKey)
                              ?? (string.IsNullOrWhiteSpace(subjectKey) ? null : subjectKey.Trim());
            if (string.IsNullOrWhiteSpace(businessKey))
                return null;

            var displayName = ReadString(root, OperationItemBusinessMetadataKeys.DisplayName)
                              ?? (string.IsNullOrWhiteSpace(displayLabel) ? null : displayLabel.Trim());
            var changeKind = ReadString(root, OperationItemBusinessMetadataKeys.ChangeKind);
            var email = ReadString(root, "email") ?? TryParseEmailFromSummary(changeSummaryJson);
            var status = ReadString(root, "status");

            DateTimeOffset observedAt = completedAtUtc ?? createdAtUtc;
            if (root.TryGetProperty(OperationItemBusinessMetadataKeys.ObservedAt, out var obsEl))
            {
                if (obsEl.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(obsEl.GetString(), out var parsed))
                    observedAt = parsed;
                else
                {
                    try { observedAt = obsEl.GetDateTimeOffset(); }
                    catch (InvalidOperationException) { /* keep fallback */ }
                    catch (FormatException) { /* keep fallback */ }
                }
            }

            return new DemoBusinessRecordObservationInput(
                RecordType: OperationItemBusinessMetadataKeys.CustomerRecordType,
                BusinessKey: businessKey,
                ModuleId: moduleId,
                IntegrationId: integrationId,
                OperationKind: operationKind,
                OperationItemId: operationItemId,
                OperationGroupId: operationGroupId,
                RunId: runId,
                DisplayName: displayName,
                Email: email,
                Status: status,
                ChangeKind: changeKind,
                ObservedAtUtc: observedAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static DemoBusinessRecordSourceObservation ToSource(DemoBusinessRecordObservationInput o)
    {
        var kind = DemoBusinessRecordSourceKinds.FromModuleId(o.ModuleId);
        var label = $"{kind} · item {o.OperationItemId} · run {o.RunId}";
        return new DemoBusinessRecordSourceObservation(
            SourceKind: kind,
            ModuleId: o.ModuleId,
            IntegrationId: o.IntegrationId,
            OperationKind: o.OperationKind,
            OperationItemId: o.OperationItemId,
            OperationGroupId: o.OperationGroupId,
            RunId: o.RunId,
            BusinessKey: o.BusinessKey,
            DisplayName: o.DisplayName,
            Email: o.Email,
            Status: o.Status,
            ChangeKind: o.ChangeKind,
            ObservedAtUtc: o.ObservedAtUtc,
            ProvenanceLabel: label);
    }

    static IReadOnlyList<DemoBusinessRecordConflict> DetectConflicts(
        IReadOnlyList<DemoBusinessRecordSourceObservation> sources)
    {
        if (sources.Count < 2)
            return [];

        var conflicts = new List<DemoBusinessRecordConflict>();
        AddFieldConflict(conflicts, sources, "email", s => NormalizeEmail(s.Email));
        AddFieldConflict(conflicts, sources, "status", s => NormalizeToken(s.Status));
        AddFieldConflict(conflicts, sources, "displayName", s => NormalizeToken(s.DisplayName));
        return conflicts;
    }

    static void AddFieldConflict(
        List<DemoBusinessRecordConflict> conflicts,
        IReadOnlyList<DemoBusinessRecordSourceObservation> sources,
        string field,
        Func<DemoBusinessRecordSourceObservation, string?> selector)
    {
        var present = sources
            .Select(s => (Source: s, Value: selector(s)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .ToArray();
        if (present.Length < 2)
            return;

        var distinct = present.Select(x => x.Value!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinct.Length < 2)
            return;

        var valuesBySource = present.ToDictionary(
            x => $"{x.Source.SourceKind}:{x.Source.ModuleId}",
            x => x.Value!,
            StringComparer.OrdinalIgnoreCase);

        conflicts.Add(new DemoBusinessRecordConflict(
            Field: field,
            ValuesBySource: valuesBySource,
            Message: $"{field} differs across sources."));
    }

    static (string? DisplayName, string? Email, string? Status) ChoosePrimary(
        IReadOnlyList<DemoBusinessRecordSourceObservation> sources)
    {
        // Prefer ApiPolling for current-state-ish fields when present; else latest observation.
        var preferred = sources
            .OrderByDescending(s => s.SourceKind == DemoBusinessRecordSourceKinds.ApiPolling)
            .ThenByDescending(s => s.ObservedAtUtc)
            .ToArray();

        string? Pick(Func<DemoBusinessRecordSourceObservation, string?> sel) =>
            preferred.Select(sel).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        return (Pick(s => s.DisplayName), Pick(s => s.Email), Pick(s => s.Status));
    }

    static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static string? TryParseEmailFromSummary(string? changeSummaryJson)
    {
        if (string.IsNullOrWhiteSpace(changeSummaryJson))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(changeSummaryJson);
            if (doc.RootElement.TryGetProperty("summary", out var summaryEl))
            {
                var summary = summaryEl.GetString();
                if (string.IsNullOrWhiteSpace(summary))
                    return null;
                const string marker = "email=";
                var idx = summary.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    return null;
                var rest = summary[(idx + marker.Length)..];
                var end = rest.IndexOf(';');
                var email = end >= 0 ? rest[..end] : rest;
                email = email.Trim();
                return string.IsNullOrWhiteSpace(email) || email == "(no-email)" ? null : email;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    static string? NormalizeEmail(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    static string? NormalizeToken(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
