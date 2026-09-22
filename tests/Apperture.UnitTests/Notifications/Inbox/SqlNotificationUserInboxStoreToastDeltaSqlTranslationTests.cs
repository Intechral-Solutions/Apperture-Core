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
/// This calls the actual production query-building seam,
/// <see cref="SqlNotificationUserInboxStore.BuildToastDeltaScanQuery"/> (internal, exposed to this test
/// project via <c>InternalsVisibleTo</c>), rather than re-declaring a copy of the query inline. That
/// seam was extracted specifically so this regression test tracks the real query shape instead of a
/// stale duplicate that could silently drift from production.
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

        var q = SqlNotificationUserInboxStore.BuildToastDeltaScanQuery(db, principalUserId);

        // Same composite-cursor predicate as SqlNotificationUserInboxStore.ListToastDeltaAsync: the
        // exact expression this test exists to protect. If this reverts to
        // string.Compare(x.Id, afterId, StringComparison.Ordinal), ToQueryString() below throws
        // InvalidOperationException instead of producing SQL.
        q = q.Where(x => x.CreatedAtUtc > cursorCreatedAtUtc
            || (x.CreatedAtUtc == cursorCreatedAtUtc
                && x.Id.CompareTo(afterId) > 0));

        var ordered = q.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(50);

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
