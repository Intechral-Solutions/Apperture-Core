using Apperture.Core.Application.Accounts;
using Apperture.Core.Domain.Identity;

namespace Apperture.UnitTests.Accounts;

/// <summary>
/// In-memory preference store for account and auth tests. Returns null for users who have never
/// saved preferences, matching the real repository so default resolution is exercised.
/// </summary>
public sealed class InMemoryUserPreferencesRepository : IUserPreferencesRepository
{
    private readonly Dictionary<string, UserPreferences> _rows = new(StringComparer.Ordinal);

    public int UpsertCount { get; private set; }

    public void Seed(string userId, UserPreferences preferences) => _rows[userId] = preferences;

    public ValueTask<UserPreferences?> GetAsync(UserId userId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_rows.TryGetValue(userId.Value, out var row) ? row : null);

    public ValueTask UpsertAsync(
        UserId userId,
        UserPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        _rows[userId.Value] = preferences;
        UpsertCount++;
        return ValueTask.CompletedTask;
    }
}
