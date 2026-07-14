using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Scheduling;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Runtime;

/// <summary>
/// SWM-2: FireTimeUtc comparisons must not use .UtcDateTime inside EF expression trees.
/// </summary>
public sealed class ScheduleRunExistsQueryTests
{
    private static readonly DateTimeOffset FireUtc = new(2025, 6, 15, 12, 1, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FireWithNonZeroOffset = new(2025, 6, 15, 14, 1, 0, TimeSpan.FromHours(2)); // same UTC instant

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    [Fact]
    public async Task RunExistsAsync_returns_true_for_existing_fire_and_false_otherwise()
    {
        await using var db = NewDb();
        db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = "sched1",
            AssignmentId = "a1",
            FireTimeUtc = FireUtc,
            StartedAtUtc = FireUtc,
            Outcome = "Dispatched"
        });
        await db.SaveChangesAsync();

        IScheduleRepository repo = new SqlScheduleRepository(db);

        (await repo.RunExistsAsync("sched1", FireUtc)).Should().BeTrue();
        (await repo.RunExistsAsync("sched1", FireWithNonZeroOffset)).Should().BeTrue(
            "same UTC instant with different offset must still match after normalization");
        (await repo.RunExistsAsync("sched1", FireUtc.AddMinutes(1))).Should().BeFalse();
        (await repo.RunExistsAsync("other", FireUtc)).Should().BeFalse();
    }

    [Fact]
    public async Task TryClaimFireAsync_second_claim_for_same_fire_returns_false()
    {
        await using var db = NewDb();
        IScheduleRepository repo = new SqlScheduleRepository(db);

        (await repo.TryClaimFireAsync("sched1", FireUtc, "a1", "host-a")).Should().BeTrue();
        (await repo.TryClaimFireAsync("sched1", FireWithNonZeroOffset, "a1", "host-b")).Should().BeFalse(
            "duplicate fire prevention must hold across offset-normalized instants");

        db.ScheduleRuns.Should().HaveCount(1);
        db.ScheduleRuns.Single().ClaimedByHostId.Should().Be("host-a");
        db.ScheduleRuns.Single().FireTimeUtc.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task GetDueAsync_does_not_throw_when_checking_existing_and_missing_fires()
    {
        await using var db = NewDb();
        db.ScheduleDefinitions.Add(new ScheduleDefinitionEntity
        {
            Id = "sched1",
            Name = "every-minute",
            RecurrenceExpression = "* * * * *",
            TimeZone = "UTC",
            Enabled = true,
            TargetJobType = (int)ScheduledJobType.WorkflowExecution,
            TargetJobConfigurationJson = "{\"workflowDefinitionId\":\"Sample.HelloWorld.HelloWorld\"}",
            Version = 1
        });
        db.ScheduleAssignments.Add(new ScheduleAssignmentEntity
        {
            Id = "a1",
            ScheduleId = "sched1",
            RuntimePlaneId = "local"
        });
        db.ScheduleStates.Add(new ScheduleStateEntity
        {
            AssignmentId = "a1",
            LastAttemptedUtc = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero)
        });
        // One prior fire already recorded mid-window
        db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = "sched1",
            AssignmentId = "a1",
            FireTimeUtc = FireUtc,
            StartedAtUtc = FireUtc,
            Outcome = "Dispatched"
        });
        await db.SaveChangesAsync();

        IScheduleEvaluator evaluator = new CronScheduleEvaluator(new SqlScheduleRepository(db));
        var asOf = new DateTimeOffset(2025, 6, 15, 12, 1, 30, TimeSpan.Zero);

        // Must not throw EF translation failures when consulting ScheduleRuns for due fires.
        var due = await evaluator.GetDueAsync(new RuntimePlaneId("local"), asOf);
        due.Should().BeEmpty("the only minute boundary in the lookback window already has a ScheduleRun");
    }

    [Fact]
    public async Task CompleteRunAsync_updates_row_using_offset_normalized_fire_time()
    {
        await using var db = NewDb();
        var repo = new SqlScheduleRepository(db);
        (await repo.TryClaimFireAsync("sched1", FireUtc, "a1", "host-a")).Should().BeTrue();

        await repo.CompleteRunAsync("sched1", FireWithNonZeroOffset, "Dispatched", null, "run-1");

        var row = db.ScheduleRuns.Single();
        row.Outcome.Should().Be("Dispatched");
        row.RunId.Should().Be("run-1");
    }
}
