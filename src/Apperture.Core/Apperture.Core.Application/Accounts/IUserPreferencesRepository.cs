using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Accounts;

/// <summary>
/// Persistence for typed per-user preferences. One row per user; absence means "platform defaults".
/// </summary>
public interface IUserPreferencesRepository
{
    /// <summary>Returns the stored preferences, or null when the user has never saved any.</summary>
    ValueTask<UserPreferences?> GetAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces the preference row for a user.</summary>
    ValueTask UpsertAsync(UserId userId, UserPreferences preferences, CancellationToken cancellationToken = default);
}
