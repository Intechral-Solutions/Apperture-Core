using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace Apperture.IntegrationTests;

/// <summary>
/// Seeds users directly in the state production creates: every <see cref="UserEntity"/> owns exactly one
/// current <see cref="UsernameLedgerEntity"/> row for its username, as <c>SqlUserRepository.AddAsync</c>
/// writes it. Pre-upgrade migration seeds (raw SQL against an older schema) deliberately do not use this.
/// </summary>
public static class UsernameLedgerTestSeeding
{
    /// <summary>Adds <paramref name="users"/> and their current username ledger rows to <paramref name="db"/>.</summary>
    public static void AddUsersWithCurrentUsernames(this AppertureDbContext db, params UserEntity[] users)
    {
        foreach (var user in users)
        {
            if (!UsernamePolicy.IsCanonical(user.Username))
                throw new ArgumentException($"Seeded user {user.Id} needs a canonical username, not '{user.Username}'.", nameof(users));

            db.Users.Add(user);
            db.UsernameLedger.Add(new UsernameLedgerEntity
            {
                Username = user.Username,
                UserId = user.Id,
                AssignedAtUtc = DateTimeOffset.UtcNow,
            });
        }
    }

    /// <summary>
    /// Ids of users that break the username foundation invariant: the user does not have exactly one
    /// current ledger row, or that row (owned by the user's own id) does not carry exactly
    /// <c>Users.Username</c>.
    /// </summary>
    public static Task<List<string>> FindUsersBreakingUsernameLedgerInvariantAsync(
        AppertureDbContext db,
        CancellationToken cancellationToken = default) =>
        db.Database.SqlQueryRaw<string>(
            """
            SELECT u.[Id] AS [Value]
            FROM [Users] u
            WHERE (SELECT COUNT(*) FROM [UsernameLedger] l WHERE l.[UserId] = u.[Id] AND l.[RetiredAtUtc] IS NULL) <> 1
               OR NOT EXISTS (
                    SELECT 1
                    FROM [UsernameLedger] l
                    WHERE l.[UserId] = u.[Id]
                      AND l.[RetiredAtUtc] IS NULL
                      AND l.[Username] COLLATE Latin1_General_100_BIN2 = u.[Username] COLLATE Latin1_General_100_BIN2)
            """)
            .ToListAsync(cancellationToken);
}
