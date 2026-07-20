using System.Reflection;
using System.Text.Json;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

/// <summary>
/// Loads embedded JSON snapshots. No network calls.
/// </summary>
public sealed class EmbeddedSampleDirectoryApiClient : ISampleDirectoryApiClient
{
    private static readonly Assembly ModuleAssembly = typeof(EmbeddedSampleDirectoryApiClient).Assembly;
    private const string ResourcePrefix = "Sample.ApiPolling.Snapshots.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public IReadOnlyList<string> ListSnapshotIds() => ApiPollingSnapshotIds.All;

    public Task<DirectorySnapshotPage> GetSnapshotAsync(
        string snapshotId,
        ApiPollQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(snapshotId))
            throw new InvalidOperationException("Snapshot id is required.");

        var id = snapshotId.Trim();
        if (!ApiPollingSnapshotIds.IsKnown(id))
        {
            throw new InvalidOperationException(
                $"Unknown snapshot id '{id}'. Known: {string.Join(", ", ApiPollingSnapshotIds.All)}.");
        }

        var resourceName = ResourcePrefix + id + ".json";
        using var stream = ModuleAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded snapshot resource '{resourceName}' was not found. Available: "
                + string.Join(", ", ModuleAssembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var doc = JsonSerializer.Deserialize<SnapshotDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Snapshot '{id}' deserialized to null.");

        var pageSize = query.PageSize <= 0 ? ApiPollingSettingDefaults.PageSize : query.PageSize;
        var offset = Math.Max(0, query.PageOffset);

        var matching = doc.Records
            .Where(r => query.IncludeDeleted || !r.IsDeleted)
            .OrderBy(r => r.ExternalId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.DisplayName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var page = matching
            .Skip(offset)
            .Take(pageSize)
            .Select(r => new DirectoryAccountRecord(
                r.ExternalId,
                r.DisplayName,
                r.Email,
                r.Status,
                r.Tier,
                r.UpdatedAtUtc,
                r.Version,
                r.IsDeleted))
            .ToList();

        var fetchedAt = DateTimeOffset.TryParse(doc.FetchedAtUtc, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

        return Task.FromResult(new DirectorySnapshotPage(id, fetchedAt, page, matching.Count));
    }

    private sealed class SnapshotDocument
    {
        public string? SnapshotId { get; set; }
        public string? FetchedAtUtc { get; set; }
        public List<SnapshotRecord> Records { get; set; } = [];
    }

    private sealed class SnapshotRecord
    {
        public string? ExternalId { get; set; }
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
        public string? Status { get; set; }
        public string? Tier { get; set; }
        public string? UpdatedAtUtc { get; set; }
        public int? Version { get; set; }
        public bool IsDeleted { get; set; }
    }
}
