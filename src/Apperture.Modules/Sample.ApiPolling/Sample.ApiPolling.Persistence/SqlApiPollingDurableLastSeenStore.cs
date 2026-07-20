using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling.Persistence;

/// <summary>
/// SQL-backed durable last-seen store. Thread-safe via per-call <see cref="IDbContextFactory{TContext}"/>.
/// </summary>
public sealed class SqlApiPollingDurableLastSeenStore : IApiPollingDurableLastSeenStore
{
    public const string FallbackScopeKey = "sample";
    public const string ModuleIdValue = "Sample.ApiPolling";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IDbContextFactory<SampleApiPollingDbContext> _dbFactory;

    public SqlApiPollingDurableLastSeenStore(IDbContextFactory<SampleApiPollingDbContext> dbFactory) =>
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public string ResolveScopeKey(string? integrationId) =>
        string.IsNullOrWhiteSpace(integrationId) ? FallbackScopeKey : integrationId.Trim();

    public void ResetScope(string scopeKey)
    {
        var key = NormalizeScope(scopeKey);
        using var db = _dbFactory.CreateDbContext();
        var state = db.ApiPollingSyncStates
            .Include(s => s.Records)
            .FirstOrDefault(s => s.ScopeKey == key);
        if (state is null)
            return;

        db.ApiPollingSyncStates.Remove(state);
        db.SaveChanges();
    }

    public IReadOnlyDictionary<string, SeenAccountFingerprint> GetLastSeen(string scopeKey)
    {
        var key = NormalizeScope(scopeKey);
        using var db = _dbFactory.CreateDbContext();
        var state = db.ApiPollingSyncStates
            .AsNoTracking()
            .Include(s => s.Records)
            .FirstOrDefault(s => s.ScopeKey == key);
        if (state is null)
            return new Dictionary<string, SeenAccountFingerprint>(StringComparer.OrdinalIgnoreCase);

        return state.Records
            .Where(r => !r.IsDeleted)
            .ToDictionary(
                r => r.ExternalId,
                r => new SeenAccountFingerprint(
                    r.ExternalId,
                    r.DisplayName,
                    r.Email,
                    r.Status,
                    r.Tier,
                    r.SourceUpdatedAtUtc,
                    r.Version,
                    r.IsDeleted,
                    r.LastSeenAtUtc),
                StringComparer.OrdinalIgnoreCase);
    }

    public void UpsertLastSeen(string scopeKey, IEnumerable<SeenAccountFingerprint> fingerprints)
    {
        ArgumentNullException.ThrowIfNull(fingerprints);
        var key = NormalizeScope(scopeKey);
        using var db = _dbFactory.CreateDbContext();
        var state = GetOrCreateState(db, key);
        var now = DateTimeOffset.UtcNow;

        foreach (var fp in fingerprints)
        {
            if (string.IsNullOrWhiteSpace(fp.ExternalId))
                continue;

            var externalId = fp.ExternalId.Trim();
            var existing = state.Records.FirstOrDefault(r =>
                string.Equals(r.ExternalId, externalId, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                existing = new ApiPollingKnownRecordEntity
                {
                    StateId = state.StateId,
                    ExternalId = externalId,
                };
                state.Records.Add(existing);
                db.ApiPollingKnownRecords.Add(existing);
            }

            existing.DisplayName = Truncate(fp.DisplayName, 256);
            existing.Email = Truncate(fp.Email, 320);
            existing.Status = Truncate(fp.Status, 64);
            existing.Tier = Truncate(fp.Tier, 64);
            existing.Version = fp.Version;
            existing.SourceUpdatedAtUtc = Truncate(fp.UpdatedAtUtc, 64);
            existing.IsDeleted = fp.IsDeleted;
            existing.RecordHash = ComputeRecordHash(fp);
            existing.LastSeenAtUtc = fp.LastSeenAtUtc == default ? now : fp.LastSeenAtUtc;
            existing.MetadataJson = null;
        }

        state.RecordCount = state.Records.Count(r => !r.IsDeleted);
        state.UpdatedAtUtc = now;
        db.SaveChanges();
    }

    public void RemoveLastSeen(string scopeKey, IEnumerable<string> externalIds)
    {
        ArgumentNullException.ThrowIfNull(externalIds);
        var key = NormalizeScope(scopeKey);
        using var db = _dbFactory.CreateDbContext();
        var state = db.ApiPollingSyncStates
            .Include(s => s.Records)
            .FirstOrDefault(s => s.ScopeKey == key);
        if (state is null)
            return;

        var ids = externalIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
            return;

        var toRemove = state.Records
            .Where(r => ids.Contains(r.ExternalId))
            .ToList();
        if (toRemove.Count == 0)
            return;

        db.ApiPollingKnownRecords.RemoveRange(toRemove);
        foreach (var r in toRemove)
            state.Records.Remove(r);
        state.RecordCount = state.Records.Count(r => !r.IsDeleted);
        state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        db.SaveChanges();
    }

    public SyncSummary? GetLastSummary(string scopeKey)
    {
        var key = NormalizeScope(scopeKey);
        using var db = _dbFactory.CreateDbContext();
        var state = db.ApiPollingSyncStates.AsNoTracking().FirstOrDefault(s => s.ScopeKey == key);
        if (state?.SummaryJson is null || string.IsNullOrWhiteSpace(state.SummaryJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<SyncSummary>(state.SummaryJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void SetLastSummary(string scopeKey, SyncSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var key = NormalizeScope(scopeKey);
        using var db = _dbFactory.CreateDbContext();
        var state = GetOrCreateState(db, key);
        var json = JsonSerializer.Serialize(summary, JsonOptions);
        if (json.Length > 2048)
            json = json[..2048];

        state.LastSnapshotId = Truncate(summary.SnapshotId, 128);
        state.LastSyncedAtUtc = summary.CompletedAtUtc;
        state.LastStatus = Truncate(summary.Status, 64);
        state.SummaryJson = json;
        state.RecordCount = db.ApiPollingKnownRecords.Count(r => r.StateId == state.StateId && !r.IsDeleted);
        state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        db.SaveChanges();
    }

    private static ApiPollingSyncStateEntity GetOrCreateState(SampleApiPollingDbContext db, string scopeKey)
    {
        var state = db.ApiPollingSyncStates
            .Include(s => s.Records)
            .FirstOrDefault(s => s.ScopeKey == scopeKey);
        if (state is not null)
            return state;

        var now = DateTimeOffset.UtcNow;
        state = new ApiPollingSyncStateEntity
        {
            StateId = Guid.NewGuid().ToString("N"),
            ScopeKey = scopeKey,
            ModuleId = ModuleIdValue,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RecordCount = 0,
        };
        db.ApiPollingSyncStates.Add(state);
        return state;
    }

    internal static string ComputeRecordHash(SeenAccountFingerprint fp)
    {
        var payload = string.Join(
            '|',
            fp.ExternalId?.Trim() ?? "",
            fp.DisplayName ?? "",
            fp.Email ?? "",
            fp.Status ?? "",
            fp.Tier ?? "",
            fp.UpdatedAtUtc ?? "",
            fp.Version?.ToString() ?? "",
            fp.IsDeleted ? "1" : "0");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }

    private static string NormalizeScope(string? scopeKey) =>
        string.IsNullOrWhiteSpace(scopeKey) ? FallbackScopeKey : scopeKey.Trim();

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
