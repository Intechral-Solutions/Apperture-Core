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

    /// <summary>
    /// Runs <paramref name="first"/> and the upsert in one transaction: if <paramref name="first"/> throws, the preferences are not
    /// written, and if the upsert fails, whatever <paramref name="first"/> wrote is rolled back with it. <paramref name="first"/>
    /// runs on the same connection, so a repository write it makes joins the transaction. It may run more than once (a transient
    /// failure reruns the whole transaction), so it must be safe to repeat.
    /// </summary>
    /// <remarks>
    /// The default runs them one after the other, which is all a store with no transactions can do.
    /// </remarks>
    async ValueTask UpsertAfterAsync(
        UserId userId,
        UserPreferences preferences,
        Func<CancellationToken, ValueTask> first,
        CancellationToken cancellationToken = default)
    {
        await first(cancellationToken).ConfigureAwait(false);
        await UpsertAsync(userId, preferences, cancellationToken).ConfigureAwait(false);
    }
}
