using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace Apperture.IntegrationTests;

/// <summary>
/// Creates the acting user an audited operation needs. The recorder reads the actor's username in the transaction it records the
/// row in, so a test that drives an audited service as a person needs that person's user, username ledger row and Local link.
/// </summary>
internal static class SqlTestUsers
{
    public static async Task<(string Id, string Username)> AddAsync(AppertureDbContext db, string stem)
    {
        var id = Guid.NewGuid().ToString("N")[..24];
        var username = $"{stem}.{Guid.NewGuid().ToString("N")[..8]}x";
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Users (Id, Username, DisplayName, IsBreakGlassEligible) VALUES ({0}, {1}, {0}, 0);"
            + " INSERT INTO UsernameLedger (Username, UserId, AssignedAtUtc) VALUES ({1}, {0}, SYSDATETIMEOFFSET());"
            + " INSERT INTO IdentityLinks (Id, UserId, Source, ExternalId, LinkedAt) VALUES ('L' + {0}, {0}, 1, {1}, SYSDATETIMEOFFSET());",
            id,
            username);
        return (id, username);
    }
}
