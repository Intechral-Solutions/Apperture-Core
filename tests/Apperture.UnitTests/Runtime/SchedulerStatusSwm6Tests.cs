using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.ControlPlane.Security;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Runtime;

/// <summary>SWM-6: scheduler heartbeat store + status classification.</summary>
public sealed class SchedulerStatusSwm6Tests
{
    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private static SchedulerDispatchHeartbeat SampleHeartbeat(
        string plane = "local",
        string host = "worker-local",
        int poll = 15,
        string? error = null,
        DateTimeOffset? updated = null) =>
        new(
            plane,
            host,
            poll,
            LastCycleStartedUtc: updated ?? DateTimeOffset.Parse("2025-06-15T12:00:00Z"),
            LastCycleCompletedUtc: updated ?? DateTimeOffset.Parse("2025-06-15T12:00:01Z"),
            LastSuccessfulCycleUtc: error is null ? updated ?? DateTimeOffset.Parse("2025-06-15T12:00:01Z") : null,
            LastErrorSummary: error,
            AssignmentCount: 2,
            DueCount: 1,
            StaleCount: 0,
            CandidateCount: 1,
            ClaimedCount: 1,
            DispatchedCount: 1,
            SkippedCount: 0,
            UpdatedUtc: updated ?? DateTimeOffset.Parse("2025-06-15T12:00:01Z"));

    [Fact]
    public async Task Heartbeat_upsert_creates_then_updates_single_row_per_plane()
    {
        await using var db = NewDb();
        var store = new SqlSchedulerDispatchHeartbeatStore(db);
        var first = SampleHeartbeat(updated: DateTimeOffset.Parse("2025-06-15T12:00:01Z"));
        await store.UpsertAsync(first);

        var second = first with
        {
            AssignmentCount = 5,
            HostId = "worker-local-b",
            UpdatedUtc = DateTimeOffset.Parse("2025-06-15T12:00:30Z")
        };
        await store.UpsertAsync(second);

        var rows = await store.ListAsync();
        rows.Should().ContainSingle();
        rows[0].AssignmentCount.Should().Be(5);
        rows[0].HostId.Should().Be("worker-local-b");
    }

    [Fact]
    public async Task Heartbeat_failure_cycle_persists_sanitized_error()
    {
        await using var db = NewDb();
        var store = new SqlSchedulerDispatchHeartbeatStore(db);
        var err = SchedulerStatusClassifier.SanitizeErrorSummary(
            new InvalidOperationException("boom Password=SuperSecret; rest"));
        err.Should().Contain("Password=***");
        err.Should().NotContain("SuperSecret");

        await store.UpsertAsync(SampleHeartbeat(error: err));
        var got = await store.GetAsync("local");
        got!.LastErrorSummary.Should().Be(err);
        got.LastSuccessfulCycleUtc.Should().BeNull();
    }

    [Fact]
    public void Classify_unknown_when_no_heartbeat()
    {
        SchedulerStatusClassifier.Classify(null, DateTimeOffset.UtcNow)
            .Should().Be(SchedulerLoopStatus.Unknown);
    }

    [Fact]
    public void Classify_healthy_when_recent_and_no_error()
    {
        var now = DateTimeOffset.Parse("2025-06-15T12:01:00Z");
        var snap = ToSnapshot(SampleHeartbeat(updated: now.AddSeconds(-10)));
        SchedulerStatusClassifier.Classify(snap, now).Should().Be(SchedulerLoopStatus.Healthy);
        SchedulerStatusClassifier.IsStale(snap, now).Should().BeFalse();
    }

    [Fact]
    public void Classify_warning_when_stale()
    {
        var now = DateTimeOffset.Parse("2025-06-15T12:05:00Z");
        var snap = ToSnapshot(SampleHeartbeat(poll: 15, updated: now.AddMinutes(-5)));
        SchedulerStatusClassifier.IsStale(snap, now).Should().BeTrue();
        SchedulerStatusClassifier.Classify(snap, now).Should().Be(SchedulerLoopStatus.Warning);
    }

    [Fact]
    public void Classify_warning_when_last_error()
    {
        var now = DateTimeOffset.Parse("2025-06-15T12:01:00Z");
        var snap = ToSnapshot(SampleHeartbeat(error: "InvalidOperationException: x", updated: now));
        SchedulerStatusClassifier.Classify(snap, now).Should().Be(SchedulerLoopStatus.Warning);
    }

    [Fact]
    public void Alignment_labels_match_expected_plane()
    {
        SchedulerStatusClassifier.ClassifyAlignment("local", "local")
            .Should().Be(SchedulerPlaneAlignment.Aligned);
        SchedulerStatusClassifier.ClassifyAlignment("local", "worker-local")
            .Should().Be(SchedulerPlaneAlignment.Mismatched);
        SchedulerStatusClassifier.ClassifyAlignment("local", null)
            .Should().Be(SchedulerPlaneAlignment.Unknown);
    }

    [Fact]
    public async Task QueryService_maps_counts_and_guidance_for_unknown()
    {
        await using var db = NewDb();
        var store = new SqlSchedulerDispatchHeartbeatStore(db);
        var svc = new SchedulerStatusQueryService(store);
        var status = await svc.GetStatusAsync("local");
        status.Status.Should().Be(SchedulerLoopStatus.Unknown);
        status.ExpectedManagementPlaneId.Should().Be("local");
        status.Guidance.Should().NotBeNullOrWhiteSpace();
        status.AssignmentCount.Should().BeNull();
    }

    [Fact]
    public async Task QueryService_maps_counts_when_heartbeat_present()
    {
        await using var db = NewDb();
        var store = new SqlSchedulerDispatchHeartbeatStore(db);
        var hb = SampleHeartbeat(updated: DateTimeOffset.UtcNow);
        await store.UpsertAsync(hb);
        var svc = new SchedulerStatusQueryService(store);
        var status = await svc.GetStatusAsync("local");
        status.Status.Should().Be(SchedulerLoopStatus.Healthy);
        status.Alignment.Should().Be(SchedulerPlaneAlignment.Aligned);
        status.ObservedHostId.Should().Be("worker-local");
        status.AssignmentCount.Should().Be(2);
        status.DueCount.Should().Be(1);
        status.ClaimedCount.Should().Be(1);
        status.DispatchedCount.Should().Be(1);
    }

    private static SchedulerDispatchHeartbeatSnapshot ToSnapshot(SchedulerDispatchHeartbeat h) =>
        new(
            h.RuntimePlaneId,
            h.HostId,
            h.PollIntervalSeconds,
            h.LastCycleStartedUtc,
            h.LastCycleCompletedUtc,
            h.LastSuccessfulCycleUtc,
            h.LastErrorSummary,
            h.AssignmentCount,
            h.DueCount,
            h.StaleCount,
            h.CandidateCount,
            h.ClaimedCount,
            h.DispatchedCount,
            h.SkippedCount,
            h.UpdatedUtc);
}
