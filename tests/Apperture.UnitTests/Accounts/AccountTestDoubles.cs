using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Shared.Kernel;
using Apperture.Core.Domain.Identity;

namespace Apperture.UnitTests.Accounts;

/// <summary>
/// User repository holding several users, so tests can prove that self-service operations touch only
/// the account they were given.
/// </summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);

    public InMemoryUserRepository(params User[] users)
    {
        foreach (var user in users)
            _users[user.Id.Value] = user;
    }

    public IReadOnlyDictionary<string, User> Users => _users;

    public User Get(string userId) => _users[userId];

    public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_users.Count);

    public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_users.TryGetValue(id.Value, out var user) ? user : null);

    public ValueTask<User?> GetByIdentityLinkAsync(
        IdentitySource source,
        string externalId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_users.Values.FirstOrDefault(u =>
            u.IdentityLinks.Any(l => l.Source == source && l.ExternalId == externalId)));

    public ValueTask AddAsync(User user, CancellationToken cancellationToken = default)
    {
        _users[user.Id.Value] = user;
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        if (!_users.ContainsKey(user.Id.Value))
            throw new InvalidOperationException($"User {user.Id.Value} not found.");
        _users[user.Id.Value] = user;
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateLastAuthenticatedAtAsync(
        IdentitySource source,
        string externalId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<(IReadOnlyList<User>, int)>((_users.Values.Skip(skip).Take(take).ToList(), _users.Count));

    public ValueTask AddIdentityLinkAsync(
        UserId userId,
        IdentitySource source,
        string externalId,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask RemoveIdentityLinkAsync(
        UserId userId,
        IdentitySource source,
        string externalId,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

/// <summary>Profile image store that records every call, so cleanup behaviour is observable.</summary>
public sealed class InMemoryUserProfileImageStore : IUserProfileImageStore
{
    private readonly List<(string UserId, string ImageKey, int Size, byte[] Content, string ContentType)> _rows = [];

    public IReadOnlyList<(string UserId, string ImageKey, int Size, byte[] Content, string ContentType)> Rows => _rows;

    public List<(string UserId, string? KeepImageKey)> DeleteCalls { get; } = [];

    public IReadOnlyList<string> KeysFor(string userId) =>
        _rows.Where(r => r.UserId == userId).Select(r => r.ImageKey).Distinct(StringComparer.Ordinal).ToList();

    public ValueTask SaveAsync(
        string userId,
        string imageKey,
        IReadOnlyList<ProfileImageRendition> renditions,
        CancellationToken cancellationToken = default)
    {
        foreach (var rendition in renditions)
            _rows.Add((userId, imageKey, rendition.Size, rendition.Content, rendition.ContentType));
        return ValueTask.CompletedTask;
    }

    public ValueTask<ProfileImageContent?> GetAsync(
        string userId,
        string imageKey,
        int size,
        CancellationToken cancellationToken = default)
    {
        var row = _rows.FirstOrDefault(r => r.UserId == userId && r.ImageKey == imageKey && r.Size == size);
        return ValueTask.FromResult<ProfileImageContent?>(
            row.Content is null ? null : new ProfileImageContent(row.ContentType, row.Content));
    }

    public ValueTask<int> DeleteForUserAsync(
        string userId,
        string? keepImageKey,
        CancellationToken cancellationToken = default)
    {
        DeleteCalls.Add((userId, keepImageKey));
        var doomed = _rows
            .Where(r => r.UserId == userId
                && (string.IsNullOrWhiteSpace(keepImageKey) || r.ImageKey != keepImageKey))
            .ToList();
        var retiredKeys = doomed.Select(r => r.ImageKey).Distinct(StringComparer.Ordinal).Count();
        foreach (var row in doomed)
            _rows.Remove(row);
        return ValueTask.FromResult(retiredKeys);
    }
}

/// <summary>Notification preference service backed by an in-memory email opt-in map.</summary>
public sealed class StubOperatorNotificationSubscriptionService : IOperatorNotificationSubscriptionService
{
    private readonly Dictionary<string, (string DisplayName, string? Description)> _definitions;
    private readonly Dictionary<(string UserId, string DefinitionId), bool> _emailEnabled = new();

    public StubOperatorNotificationSubscriptionService(
        params (string DefinitionId, string DisplayName, string? Description)[] definitions)
    {
        _definitions = definitions.ToDictionary(d => d.DefinitionId, d => (d.DisplayName, d.Description), StringComparer.Ordinal);
    }

    public List<(string UserId, string DefinitionId, bool EmailEnabled)> SetCalls { get; } = [];

    public void SeedEmailEnabled(string userId, string definitionId, bool enabled) =>
        _emailEnabled[(userId, definitionId)] = enabled;

    public bool IsEmailEnabled(string userId, string definitionId) =>
        _emailEnabled.TryGetValue((userId, definitionId), out var value) && value;

    public ValueTask<IReadOnlyList<OperatorEmailSubscriptionPreference>> ListEmailPreferencesAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<OperatorEmailSubscriptionPreference>>(
            _definitions.Select(d => Build(userId, d.Key)).ToList());

    public ValueTask<OperatorEmailSubscriptionPreference> SetEmailEnabledAsync(
        string userId,
        string definitionId,
        bool emailEnabled,
        CancellationToken cancellationToken = default)
    {
        if (!_definitions.ContainsKey(definitionId))
        {
            throw new KeyNotFoundException(
                $"Notification definition '{definitionId}' is not available for operator email preferences.");
        }

        SetCalls.Add((userId, definitionId, emailEnabled));
        _emailEnabled[(userId, definitionId)] = emailEnabled;
        return ValueTask.FromResult(Build(userId, definitionId));
    }

    private OperatorEmailSubscriptionPreference Build(string userId, string definitionId)
    {
        var (displayName, description) = _definitions[definitionId];
        var enabled = IsEmailEnabled(userId, definitionId);
        return new OperatorEmailSubscriptionPreference(
            definitionId,
            displayName,
            description,
            enabled,
            HasSelfSubscription: enabled,
            HasGlobalEmailOptOut: !enabled);
    }
}

/// <summary>Audit sink that keeps every appended record for assertions.</summary>
public sealed class RecordingAuditService : IAuditService
{
    public List<AuditRecord> Records { get; } = [];

    public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        Records.Add(record);
        return ValueTask.CompletedTask;
    }

    public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new AuditPage([], null));

    public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(0);

    public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Records.FirstOrDefault(r => r.Id.Value == id));

    public bool HasAction(string action) => Records.Any(r => r.Action == action);
}
