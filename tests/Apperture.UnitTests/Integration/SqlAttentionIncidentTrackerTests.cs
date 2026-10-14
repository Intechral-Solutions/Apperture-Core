using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Domain.Integration;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Integration;

public sealed class SqlAttentionIncidentTrackerTests
{
    private static readonly IntegrationId IntId = new("int-e68");

    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private static ScheduleExpectationSnapshot Snapshot(ScheduleExpectationEvaluationStatus status, DateTimeOffset? lastFire = null) =>
        new(
            new ScheduleExpectationEvaluation(
                ExpectsRecurringRuns: true,
                HasExpectedFireInWindow: true,
                ExpectedFireCountInWindow: 1,
                MostRecentExpectedFireUtc: lastFire,
                InGracePeriod: false,
                SatisfiedByAuthorizedScheduleOriginRun: false,
                LastSatisfyingScheduleOriginRunId: null,
                LastSatisfyingScheduleOriginRunAt: null,
                Status: status,
                OperatorSummaryLine: "test"),
            ReferenceScheduleId: "sch1",
            LatestScheduleOriginAuthorizedRun: null,
            EvidenceWindowStartUtc: DateTimeOffset.Parse("2025-01-01T00:00:00Z"));

    [Fact]
    public async Task Observe_missing_opens_active_row()
    {
        await using var db = CreateDb();
        var sut = new SqlAttentionIncidentTracker(db);
        var t0 = DateTimeOffset.Parse("2025-01-10T12:00:00Z");
        var fire = DateTimeOffset.Parse("2025-01-10T11:00:00Z");

        await sut.ObserveScheduleExpectationAsync(IntId, Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun, fire), t0);

        var rows = await sut.ListRecentForIntegrationAsync(
            IntId,
            AttentionConditionKeys.ExpectedRunMissing,
            10,
            CancellationToken.None);
        rows.Should().ContainSingle();
        rows[0].Status.Should().Be(AttentionIncidentStatuses.Active);
        rows[0].StartedAtUtc.Should().Be(t0);
        rows[0].LastObservedAtUtc.Should().Be(t0);
        rows[0].FirstExpectedFireAtUtc.Should().Be(fire);
        rows[0].LastExpectedFireAtUtc.Should().Be(fire);
    }

    [Fact]
    public async Task Observe_still_missing_updates_observed_and_optional_first_fire()
    {
        await using var db = CreateDb();
        var sut = new SqlAttentionIncidentTracker(db);
        var t0 = DateTimeOffset.Parse("2025-01-10T12:00:00Z");
        var t1 = DateTimeOffset.Parse("2025-01-10T13:00:00Z");
        var fire0 = DateTimeOffset.Parse("2025-01-10T11:00:00Z");
        var fire1 = DateTimeOffset.Parse("2025-01-10T10:00:00Z");

        await sut.ObserveScheduleExpectationAsync(IntId, Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun, fire0), t0);
        await sut.ObserveScheduleExpectationAsync(IntId, Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun, fire1), t1);

        var row = (await sut.ListRecentForIntegrationAsync(IntId, AttentionConditionKeys.ExpectedRunMissing, 10, CancellationToken.None))
            .Should().ContainSingle().Subject;
        row.LastObservedAtUtc.Should().Be(t1);
        row.FirstExpectedFireAtUtc.Should().Be(fire0);
        row.LastExpectedFireAtUtc.Should().Be(fire1);
    }

    [Fact]
    public async Task Observe_cleared_resolves_active()
    {
        await using var db = CreateDb();
        var sut = new SqlAttentionIncidentTracker(db);
        var t0 = DateTimeOffset.Parse("2025-01-10T12:00:00Z");
        var t1 = DateTimeOffset.Parse("2025-01-10T13:00:00Z");

        await sut.ObserveScheduleExpectationAsync(IntId, Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun), t0);
        await sut.ObserveScheduleExpectationAsync(IntId, Snapshot(ScheduleExpectationEvaluationStatus.Satisfied), t1);

        var row = (await sut.ListRecentForIntegrationAsync(IntId, AttentionConditionKeys.ExpectedRunMissing, 10, CancellationToken.None))
            .Should().ContainSingle().Subject;
        row.Status.Should().Be(AttentionIncidentStatuses.Resolved);
        row.ResolvedAtUtc.Should().Be(t1);
        row.ResolutionReason.Should().Be(AttentionIncidentResolutionReasons.ExpectationCleared);
    }

    [Fact]
    public async Task Observation_and_resolution_leave_an_acknowledgement_made_in_between_untouched()
    {
        var options = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppertureDbContext(options);
        var sut = new SqlAttentionIncidentTracker(db);
        var t0 = DateTimeOffset.Parse("2025-01-10T12:00:00Z");
        var ackAt = DateTimeOffset.Parse("2025-01-10T12:30:00Z");
        await sut.ObserveScheduleExpectationAsync(IntId, Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun), t0);
        var id = (await sut.ListRecentForIntegrationAsync(IntId, AttentionConditionKeys.ExpectedRunMissing, 1, CancellationToken.None))[0].Id;
        // The acknowledgement is another writer's (AttentionIncidentAcknowledgementService); the tracker must not overwrite it.
        await using (var other = new AppertureDbContext(options))
        {
            var row = await other.AttentionIncidents.SingleAsync(e => e.Id == id);
            row.AcknowledgedAtUtc = ackAt;
            row.AcknowledgedBy = "actor-1";
            row.AcknowledgementNote = "seen";
            await other.SaveChangesAsync();
        }

        await sut.ObserveScheduleExpectationAsync(IntId, Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun), t0.AddMinutes(45));
        await sut.ObserveScheduleExpectationAsync(IntId, Snapshot(ScheduleExpectationEvaluationStatus.Satisfied), t0.AddHours(1));

        var resolved = (await sut.ListRecentForIntegrationAsync(IntId, AttentionConditionKeys.ExpectedRunMissing, 1, CancellationToken.None))[0];
        resolved.Status.Should().Be(AttentionIncidentStatuses.Resolved);
        resolved.AcknowledgedAtUtc.Should().Be(ackAt);
        resolved.AcknowledgedBy.Should().Be("actor-1");
        resolved.AcknowledgementNote.Should().Be("seen");
    }

    [Fact]
    public async Task GetIntegrationIdsWithActiveConditionAsync_returns_only_active()
    {
        await using var db = CreateDb();
        var sut = new SqlAttentionIncidentTracker(db);
        var t0 = DateTimeOffset.Parse("2025-01-10T12:00:00Z");
        var t1 = DateTimeOffset.Parse("2025-01-10T13:00:00Z");
        await sut.ObserveScheduleExpectationAsync(IntId, Snapshot(ScheduleExpectationEvaluationStatus.MissingExpectedRun), t0);
        await sut.ObserveScheduleExpectationAsync(IntId, Snapshot(ScheduleExpectationEvaluationStatus.Satisfied), t1);

        var active = await sut.GetIntegrationIdsWithActiveConditionAsync(AttentionConditionKeys.ExpectedRunMissing, CancellationToken.None);
        active.Should().BeEmpty();
    }
}
