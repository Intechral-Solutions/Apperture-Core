using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// SWM-2: proves RunExistsAsync EF predicate translates on SQL Server (regression for .UtcDateTime).
/// Without a reachable test SQL endpoint the test returns early in optional mode and fails when
/// APPERTURE_REQUIRE_SQL_TESTS=1 (see <see cref="SqlTestExecution"/>).
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ScheduleRunExistsSqlServerTests
{
    private static readonly DateTimeOffset FireUtc = new(2026, 7, 14, 16, 5, 0, TimeSpan.Zero);

    private static async Task<AppertureDbContext?> TryCreateMigratedDbAsync()
    {
        var cs = SqlTestExecution.ConnectionStringOrNotExecuted();
        if (cs is null)
            return null;

        try
        {
            SqlServerTestConfiguration.EnsureTestDatabaseExists(cs);
        }
        catch (InvalidOperationException)
        {
            SqlTestExecution.NotExecuted("the test SQL Server could not be reached to create the catalog");
            return null;
        }

        var options = new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(cs)
            .Options;

        var db = new AppertureDbContext(options);
        if (!await db.Database.CanConnectAsync())
        {
            await db.DisposeAsync();
            SqlTestExecution.NotExecuted("the test catalog was created but cannot be connected to");
            return null;
        }

        await AppertureDatabaseMigrator.MigrateAsync(db);
        return db;
    }

    [Fact]
    public async Task RunExistsAsync_translates_on_sql_server_and_detects_duplicates()
    {
        await using var db = await TryCreateMigratedDbAsync();
        if (db is null)
            return; // optional mode only: TryCreateMigratedDbAsync has declared it (SqlTestExecution.NotExecuted)

        var scheduleId = "swm2-" + Guid.NewGuid().ToString("N")[..12];
        db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = scheduleId,
            AssignmentId = "a-swm2",
            FireTimeUtc = FireUtc,
            StartedAtUtc = FireUtc,
            Outcome = "Dispatched",
            ClaimedByHostId = "worker-local"
        });
        await db.SaveChangesAsync();

        IScheduleRepository repo = new SqlScheduleRepository(db);

        // Must not throw InvalidOperationException translation failure.
        var exists = await repo.RunExistsAsync(scheduleId, FireUtc);
        exists.Should().BeTrue();

        var existsOffsetEquivalent = await repo.RunExistsAsync(
            scheduleId,
            FireUtc.ToOffset(TimeSpan.FromHours(2)));
        existsOffsetEquivalent.Should().BeTrue();

        (await repo.TryClaimFireAsync(scheduleId, FireUtc, "a-swm2", "other-host")).Should().BeFalse();

        db.ScheduleRuns.RemoveRange(db.ScheduleRuns.Where(r => r.ScheduleId == scheduleId));
        await db.SaveChangesAsync();
    }
}
