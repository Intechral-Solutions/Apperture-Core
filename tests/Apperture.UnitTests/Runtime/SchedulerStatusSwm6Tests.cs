using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.ControlPlane.Security;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Runtime;

/// <summary>SWM-6 + SWM-7: scheduler heartbeat store, classification, and worker runtime projection.</summary>
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
        DateTimeOffset? updated = null,
        DateTimeOffset? processStarted = null,
        string? applicationVersion = null,
        string? environmentName = null) =>
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
            UpdatedUtc: updated ?? DateTimeOffset.Parse("2025-06-15T12:00:01Z"),
            ProcessStartedUtc: processStarted,
            ApplicationVersion: applicationVersion,
            EnvironmentName: environmentName);

    [Fact]
    public async Task Heartbeat_upsert_updates_same_plane_and_host_row()
    {
        await using var db = NewDb();
        var store = new SqlSchedulerDispatchHeartbeatStore(db);
        var first = SampleHeartbeat(host: "worker-local", updated: DateTimeOffset.Parse("2025-06-15T12:00:01Z"));
        await store.UpsertAsync(first);

        var second = first with
        {
            AssignmentCount = 5,
            UpdatedUtc = DateTimeOffset.Parse("2025-06-15T12:00:30Z")
        };
        await store.UpsertAsync(second);

        var rows = await store.ListAsync();
        rows.Should().ContainSingle();
        rows[0].AssignmentCount.Should().Be(5);
        rows[0].HostId.Should().Be("worker-local");
    }

    [Fact]
    public async Task Heartbeat_upsert_creates_separate_rows_for_different_hosts_same_plane()
    {
        await using var db = NewDb();
        var store = new SqlSchedulerDispatchHeartbeatStore(db);
        await store.UpsertAsync(SampleHeartbeat(host: "worker-a", updated: DateTimeOffset.Parse("2025-06-15T12:00:01Z")));
        await store.UpsertAsync(SampleHeartbeat(host: "worker-b", updated: DateTimeOffset.Parse("2025-06-15T12:00:30Z")));

        var rows = await store.ListByPlaneAsync("local");
        rows.Should().HaveCount(2);
        rows[0].HostId.Should().Be("worker-b");
        rows[1].HostId.Should().Be("worker-a");

        var primary = await store.GetAsync("local");
        primary!.HostId.Should().Be("worker-b");
        primary.UpdatedUtc.Should().Be(DateTimeOffset.Parse("2025-06-15T12:00:30Z"));
    }

    [Fact]
    public async Task Heartbeat_upsert_persists_optional_worker_metadata()
    {
        await using var db = NewDb();
        var store = new SqlSchedulerDispatchHeartbeatStore(db);
        var started = DateTimeOffset.Parse("2025-06-15T11:55:00Z");
        await store.UpsertAsync(SampleHeartbeat(
            updated: DateTimeOffset.Parse("2025-06-15T12:00:01Z"),
            processStarted: started,
            applicationVersion: "1.2.3-test",
            environmentName: "Development"));

        var got = await store.GetAsync("local");
        got!.ProcessStartedUtc.Should().Be(started);
        got.ApplicationVersion.Should().Be("1.2.3-test");
        got.EnvironmentName.Should().Be("Development");
    }

    [Fact]
    public async Task Heartbeat_upsert_allows_null_metadata()
    {
        await using var db = NewDb();
        var store = new SqlSchedulerDispatchHeartbeatStore(db);
        await store.UpsertAsync(SampleHeartbeat(updated: DateTimeOffset.UtcNow));
        var got = await store.GetAsync("local");
        got!.ProcessStartedUtc.Should().BeNull();
        got.ApplicationVersion.Should().BeNull();
        got.EnvironmentName.Should().BeNull();
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
    public void BuildWorkerRuntime_unknown_includes_guidance()
    {
        var now = DateTimeOffset.Parse("2025-06-15T12:01:00Z");
        var worker = SchedulerStatusClassifier.BuildWorkerRuntime("local", null, now);
        worker.InventoryModel.Should().Be(WorkerRuntimeInventoryModel.SingleHostObserved);
        worker.Status.Should().Be(SchedulerLoopStatus.Unknown);
        worker.Alignment.Should().Be(SchedulerPlaneAlignment.Unknown);
        worker.SchedulerStatus.Should().Be(SchedulerLoopStatus.Unknown);
        worker.HostId.Should().BeNull();
        worker.Guidance.Should().Contain("Manual recovery");
        worker.ProcessStartedUtc.Should().BeNull();
        worker.ApplicationVersion.Should().BeNull();
        worker.EnvironmentName.Should().BeNull();
    }

    [Fact]
    public void BuildWorkerRuntime_healthy_maps_metadata_and_last_seen()
    {
        var now = DateTimeOffset.Parse("2025-06-15T12:01:00Z");
        var started = DateTimeOffset.Parse("2025-06-15T11:00:00Z");
        var hb = SampleHeartbeat(updated: now.AddSeconds(-5), processStarted: started, applicationVersion: "9.9.9", environmentName: "Development");
        var worker = SchedulerStatusClassifier.BuildWorkerRuntime("local", ToSnapshot(hb), now, hb.ProcessStartedUtc, hb.ApplicationVersion, hb.EnvironmentName);
        worker.Status.Should().Be(SchedulerLoopStatus.Healthy);
        worker.Alignment.Should().Be(SchedulerPlaneAlignment.Aligned);
        worker.HostId.Should().Be("worker-local");
        worker.RuntimePlaneId.Should().Be("local");
        worker.LastSeenUtc.Should().Be(hb.UpdatedUtc);
        worker.ProcessStartedUtc.Should().Be(started);
        worker.ApplicationVersion.Should().Be("9.9.9");
        worker.EnvironmentName.Should().Be("Development");
        worker.Guidance.Should().BeNull();
    }

    [Fact]
    public void BuildGuidance_stale_mentions_write_failure_aging()
    {
        var guidance = SchedulerStatusClassifier.BuildGuidance(SchedulerLoopStatus.Warning, isStale: true, SchedulerPlaneAlignment.Aligned, lastError: null);
        guidance.Should().Contain("best-effort");
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
        status.WorkerRuntime.Status.Should().Be(SchedulerLoopStatus.Unknown);
        status.WorkerRuntime.InventoryModel.Should().Be(WorkerRuntimeInventoryModel.SingleHostObserved);
        status.WorkerRuntime.Guidance.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task QueryService_maps_counts_and_worker_runtime_when_heartbeat_present()
    {
        await using var db = NewDb();
        var store = new SqlSchedulerDispatchHeartbeatStore(db);
        var hb = SampleHeartbeat(
            updated: DateTimeOffset.UtcNow,
            processStarted: DateTimeOffset.UtcNow.AddHours(-1),
            applicationVersion: "build-42",
            environmentName: "Development");
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
        status.WorkerRuntime.Status.Should().Be(SchedulerLoopStatus.Healthy);
        status.WorkerRuntime.HostId.Should().Be("worker-local");
        status.WorkerRuntime.RuntimePlaneId.Should().Be("local");
        status.WorkerRuntime.ApplicationVersion.Should().Be("build-42");
        status.WorkerRuntime.EnvironmentName.Should().Be("Development");
        status.WorkerRuntime.ProcessStartedUtc.Should().NotBeNull();
        status.WorkerRuntime.LastSeenUtc.Should().NotBeNull();
        status.ObservedWorkerCount.Should().Be(1);
        status.ObservedWorkers.Should().ContainSingle(w => w.HostId == "worker-local");
        status.StaleObservedWorkerCount.Should().Be(0);
        status.WorkerRuntime.InventoryModel.Should().Be(WorkerRuntimeInventoryModel.SingleHostObserved);
    }

    [Fact]
    public async Task QueryService_multi_host_selects_latest_and_lists_observed()
    {
        await using var db = NewDb();
        var store = new SqlSchedulerDispatchHeartbeatStore(db);
        var older = DateTimeOffset.UtcNow.AddMinutes(-2);
        var newer = DateTimeOffset.UtcNow.AddSeconds(-5);
        await store.UpsertAsync(SampleHeartbeat(host: "worker-old", updated: older));
        await store.UpsertAsync(SampleHeartbeat(host: "worker-new", updated: newer));

        var svc = new SchedulerStatusQueryService(store);
        var status = await svc.GetStatusAsync("local");

        status.ObservedHostId.Should().Be("worker-new");
        status.ObservedWorkerCount.Should().Be(2);
        status.WorkerRuntime.InventoryModel.Should().Be(WorkerRuntimeInventoryModel.MultiHostObserved);
        status.ObservedWorkers.Should().HaveCount(2);
        status.ObservedWorkers![0].HostId.Should().Be("worker-new");
        status.ObservationGuidance.Should().Contain("2 worker hosts");
        status.ObservationGuidance.Should().Contain("externally managed");
    }

    [Fact]
    public void Observation_guidance_covers_none_and_stale()
    {
        SchedulerStatusClassifier.BuildObservationSummaryGuidance(0, 0, false, false)
            .Should().Contain("No workers observed");
        SchedulerStatusClassifier.BuildObservationSummaryGuidance(2, 1, true, true)
            .Should().Contain("stale")
            .And.Contain("plane")
            .And.Contain("HostId");
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
