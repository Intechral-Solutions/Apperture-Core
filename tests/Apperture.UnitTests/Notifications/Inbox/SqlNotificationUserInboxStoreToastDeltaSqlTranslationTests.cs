using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications.Inbox;

/// <summary>
/// Guards the SQL-translation-safety of <see cref="SqlNotificationUserInboxStore.ListToastDeltaAsync"/>'s
/// composite (CreatedAtUtc, Id) cursor predicate. The other tests in
/// <see cref="SqlNotificationUserInboxStoreToastDeltaTests"/> all run against
/// <c>UseInMemoryDatabase</c>, which never parses LINQ into SQL the way a relational provider does, so
/// they would not notice a change (e.g. reverting <c>x.Id.CompareTo(afterId)</c> back to
/// <c>string.Compare(x.Id, afterId, StringComparison.Ordinal)</c>, which EF Core cannot translate and
/// throws <see cref="InvalidOperationException"/>) that broke SQL Server translation or silently
/// introduced client-side evaluation of the cursor predicate.
///
/// This test configures an <see cref="AppertureDbContext"/> against the SQL Server provider (via
/// <c>UseSqlServer</c>) but never opens a connection — <c>ToQueryString()</c> only needs the provider's
/// query compiler, not a live database — and asserts on the shape of the generated SQL. This mirrors the
/// existing "configure SqlServer provider without connecting" pattern used by
/// <c>AppertureDbContextModelConsistencyTests</c>.
///
/// The query built here intentionally mirrors the shape <see cref="SqlNotificationUserInboxStore"/>
/// builds internally (joins, DefaultIfEmpty, composite cursor predicate, composite ordering) rather than
/// calling the production method directly, since <c>ListToastDeltaAsync</c> awaits
/// <c>ToListAsync</c> internally rather than exposing an inspectable <see cref="IQueryable{T}"/>.
/// </summary>
public sealed class SqlNotificationUserInboxStoreToastDeltaSqlTranslationTests
{
    private static AppertureDbContext CreateSqlServerDb() =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .UseSqlServer("Server=(local);Database=unused;Trusted_Connection=True;")
            .Options);

    [Fact]
    public void Composite_cursor_predicate_translates_to_a_single_SQL_query_with_both_LEFT_JOINs()
    {
        using var db = CreateSqlServerDb();

        const string principalUserId = "user-a";
        var cursorCreatedAtUtc = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
        const string afterId = "inbox-cursor";

        var q = from inbox in db.NotificationUserInboxItems.AsNoTracking()
                join inst in db.NotificationInstances.AsNoTracking()
                    on inbox.NotificationInstanceId equals inst.Id
                join def in db.NotificationDefinitions.AsNoTracking()
                    on inst.NotificationDefinitionId equals def.Id into defJoin
                from def in defJoin.DefaultIfEmpty()
                join pref in db.NotificationUserPreferences.AsNoTracking()
                    on new { UserId = inbox.PrincipalUserId, DefinitionId = inst.NotificationDefinitionId, Channel = NotificationChannels.Toast }
                    equals new { UserId = pref.UserId, DefinitionId = pref.NotificationDefinitionId, Channel = pref.Channel }
                    into prefJoin
                from pref in prefJoin.DefaultIfEmpty()
                where inbox.PrincipalUserId == principalUserId
                select new { inbox, inst, def, pref };

        // Same composite-cursor predicate as SqlNotificationUserInboxStore.ListToastDeltaAsync: the
        // exact expression this test exists to protect. If this reverts to
        // string.Compare(x.inbox.Id, afterId, StringComparison.Ordinal), ToQueryString() below throws
        // InvalidOperationException instead of producing SQL.
        q = q.Where(x => x.inbox.CreatedAtUtc > cursorCreatedAtUtc
            || (x.inbox.CreatedAtUtc == cursorCreatedAtUtc
                && x.inbox.Id.CompareTo(afterId) > 0));

        var ordered = q.OrderBy(x => x.inbox.CreatedAtUtc).ThenBy(x => x.inbox.Id).Take(50);

        // The primary assertion: translation does not throw. A genuinely untranslatable expression (like
        // the old string.Compare form) fails here with InvalidOperationException, not silently.
        var sql = ordered.ToQueryString();

        // The composite predicate reached SQL: both the CreatedAtUtc comparison and the Id tie-break
        // comparison are present in the generated text (not asserting exact formatting/whitespace, which
        // can shift harmlessly across EF Core minor versions).
        sql.Should().Contain("CreatedAtUtc");
        sql.Should().Contain("[Id]");
        sql.Should().MatchRegex(@"\[.*CreatedAtUtc.*\]\s*>");

        // Both LEFT JOINs (NotificationDefinitions, NotificationUserPreferences) were pushed to SQL
        // rather than evaluated client-side.
        sql.Should().Contain("LEFT JOIN");
        sql.Should().Contain("[NotificationDefinitions]");
        sql.Should().Contain("[NotificationUserPreferences]");

        var leftJoinCount = System.Text.RegularExpressions.Regex.Matches(sql, "LEFT JOIN").Count;
        leftJoinCount.Should().BeGreaterThanOrEqualTo(2, "both the definition and preference joins should be LEFT JOINs pushed down to SQL");
    }
}
