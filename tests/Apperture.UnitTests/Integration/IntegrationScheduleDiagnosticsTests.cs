using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Integration;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;
using Apperture.Core.Domain.Scheduling;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Integration;

public sealed class IntegrationScheduleDiagnosticsTests
{
    private sealed class FixedUtcTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static readonly IntegrationId IntId = new("int01");
    private const string ScheduleId = "sch01";

    private static IntegrationReadinessProjection RunnableReadiness() =>
        new(
            IntId.Value,
            "Sample.Mod",
            IsRunnable: true,
            BlockingIssueCount: 0,
            AdvisoryIssueCount: 0,
            Array.Empty<IntegrationReadinessIssue>());

    private static ScheduleDefinition HourlyEnabledSchedule(
        string? lifecycleEnabledAtUtc = null)
    {
        var lifecycle = lifecycleEnabledAtUtc is { } s
            ? new ScheduleLifecycleEvidence(DateTimeOffset.Parse(s), null, DateTimeOffset.Parse(s))
            : null;

        return new ScheduleDefinition(
            new ScheduleId(ScheduleId),
            "N",
            "0 * * * *",
            "UTC",
            Enabled: true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            null,
            Version: 1,
            IntegrationId: IntId,
            Lifecycle: lifecycle);
    }

    private static string? ReferenceScheduleIdForDiagnostics(
        IReadOnlyList<ScheduleDefinition> schedules,
        IntegrationId integrationId) =>
        schedules
            .Where(s =>
                s.IntegrationId == integrationId
                && !string.IsNullOrWhiteSpace(s.RecurrenceExpression)
                && (s.Target.JobType == ScheduledJobType.WorkflowExecution
                    || s.Target.JobType == ScheduledJobType.ModuleOperationExecution))
            .OrderBy(s => s.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault()?.Id.Value;

    /// <summary>
    /// Mirrors pre–Epic 68 diagnostics window semantics (observation lookback as window start) so tests stay aligned with fleet when Active.
    /// </summary>
    private static ScheduleExpectationSnapshot BuildScheduleExpectationSnapshotForTest(
        DomainIntegration integration,
        IntegrationReadinessProjection readiness,
        IReadOnlyList<ScheduleDefinition> schedules,
        DateTimeOffset utcNow,
        FleetScheduleOriginRunEvidence? latestScheduleOrigin)
    {
        var observationWindowStartUtc = utcNow - IntegrationFleetRunEvidence.Lookback;
        var attention = IntegrationFleetAttention.Compute(integration.State.ToString(), readiness);
        var latestEvidence = latestScheduleOrigin is null
            ? null
            : new ScheduleOriginRunEvidenceInput(latestScheduleOrigin.AuthorizedRunId, latestScheduleOrigin.AuthorizedRunAt);
        var evalCtx = new ScheduleExpectationEvaluationContext(
            integration.State.ToString(),
            utcNow,
            observationWindowStartUtc,
            integration.CreatedAt,
            attention,
            readiness,
            integration.Id,
            schedules,
            latestEvidence);
        var evaluation = new ScheduleExpectationEvaluator().Evaluate(evalCtx);
        var refSched = ReferenceScheduleIdForDiagnostics(schedules, integration.Id);
        return new ScheduleExpectationSnapshot(evaluation, refSched, latestEvidence, observationWindowStartUtc);
    }

    private static IReadOnlyList<DateTimeOffset> RecentExpectedFires(DateTimeOffset utcNow)
    {
        var timelineFromUtc = utcNow - TimeSpan.FromDays(30);
        var schedule = HourlyEnabledSchedule();
        var set = new SortedSet<DateTime>();
        ScheduleExpectedFireCollector.TryCollectFires(
            schedule,
            timelineFromUtc.UtcDateTime,
            utcNow.UtcDateTime,
            set);

        return set
            .OrderByDescending(t => t)
            .Take(10)
            .Select(t => new DateTimeOffset(t, TimeSpan.Zero))
            .ToList();
    }

    private static IntegrationScheduleDiagnosticsService CreateService(
        DateTimeOffset utcNow,
        Mock<IRunQueryService> runQuery,
        Mock<IIntegrationManagementService> integrationManagement,
        Mock<IIntegrationReadinessService> readiness,
        Mock<IScheduleRepository> scheduleRepo)
    {
        var attention = new Mock<IAttentionIncidentTracker>();
        attention.Setup(x => x.ListRecentForIntegrationAsync(
                It.IsAny<IntegrationId>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AttentionIncidentProjection>());
        var service = new IntegrationScheduleDiagnosticsService(
            integrationManagement.Object,
            readiness.Object,
            scheduleRepo.Object,
            runQuery.Object,
            attention.Object,
            new FixedUtcTimeProvider(utcNow));
        return service;
    }

    [Fact]
    public async Task Diagnostics_when_expected_satisfied_returns_Satisfied_and_attaches_run()
    {
        var utcNow = DateTimeOffset.Parse("2025-01-15T12:30:00Z");
        var integrationCreatedAt = utcNow - TimeSpan.FromDays(10);
        var expectedFires = RecentExpectedFires(utcNow);
        expectedFires.Should().NotBeEmpty();
        var mostRecentExpectedFire = expectedFires[0];

        var latestRunAt = mostRecentExpectedFire.AddMinutes(5);
        var runId = "run-satisfied-1";

        var runQuery = new Mock<IRunQueryService>();
        runQuery.Setup(x => x.GetFleetOperationalRunSignalsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> ids, DateTimeOffset _, int _, CancellationToken _) =>
            {
                var d = new Dictionary<string, FleetOperationalRunSignals>(StringComparer.Ordinal)
                {
                    [ids[0]] = new FleetOperationalRunSignals(
                        0,
                        null,
                        null,
                        runId,
                        "completed",
                        latestRunAt)
                };
                return d;
            });

        runQuery.Setup(x => x.GetLatestAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> ids, DateTimeOffset sinceUtc, CancellationToken _) =>
            {
                return new Dictionary<string, FleetScheduleOriginRunEvidence>(StringComparer.Ordinal)
                {
                    [ids[0]] = new FleetScheduleOriginRunEvidence(runId, latestRunAt)
                };
            });

        runQuery.Setup(x => x.GetAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> ids, DateTimeOffset _, int _, CancellationToken _) =>
                new Dictionary<string, IReadOnlyList<FleetScheduleOriginRunEvidence>>(StringComparer.Ordinal)
                {
                    [ids[0]] = new List<FleetScheduleOriginRunEvidence> { new(runId, latestRunAt) }
                });

        var integration = new DomainIntegration(
            IntId,
            "My integration",
            "Sample.Mod",
            null,
            IntegrationState.Active,
            null,
            integrationCreatedAt,
            utcNow);
        var schedules = new List<ScheduleDefinition> { HourlyEnabledSchedule() };

        var integrationManagement = new Mock<IIntegrationManagementService>();
        integrationManagement.Setup(x => x.GetIntegrationAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);

        var readinessSvc = new Mock<IIntegrationReadinessService>();
        readinessSvc.Setup(x => x.EvaluateScheduleExpectationAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildScheduleExpectationSnapshotForTest(
                integration,
                RunnableReadiness(),
                schedules,
                utcNow,
                new FleetScheduleOriginRunEvidence(runId, latestRunAt)));

        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedules);

        var sut = CreateService(utcNow, runQuery, integrationManagement, readinessSvc, schedRepo);
        var diag = await sut.GetDiagnosticsAsync(IntId);
        diag.Should().NotBeNull();
        diag!.EvaluationStatus.Should().Be(ScheduleExpectationEvaluationStatus.Satisfied);
        diag.LastSatisfyingScheduleOriginAuthorizedRunId.Should().Be(runId);
        diag.LastSatisfyingScheduleOriginAuthorizedRunAt.Should().Be(latestRunAt);
        diag.RecentExpectedFires.Should().NotBeEmpty();
        diag.RecentExpectedFires[0].Outcome.Should().Be(ExpectedFireDiagnosticOutcome.Satisfied);
        diag.RecentExpectedFires[0].SatisfyingScheduleOriginAuthorizedRunId.Should().Be(runId);
    }

    [Fact]
    public async Task Diagnostics_when_expected_missing_returns_MissingExpectedRun_and_timeline_Missing()
    {
        var utcNow = DateTimeOffset.Parse("2025-01-15T12:30:00Z");
        var integrationCreatedAt = utcNow - TimeSpan.FromDays(10);
        var expectedFires = RecentExpectedFires(utcNow);
        expectedFires.Should().NotBeEmpty();
        var mostRecentExpectedFire = expectedFires[0];

        var runQuery = new Mock<IRunQueryService>();
        runQuery.Setup(x => x.GetFleetOperationalRunSignalsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> ids, DateTimeOffset _, int _, CancellationToken _) =>
            {
                var d = new Dictionary<string, FleetOperationalRunSignals>(StringComparer.Ordinal);
                d[ids[0]] = new FleetOperationalRunSignals(0, null, null, null, null, null);
                return d;
            });

        runQuery.Setup(x => x.GetLatestAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, FleetScheduleOriginRunEvidence>(StringComparer.Ordinal));

        runQuery.Setup(x => x.GetAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyList<FleetScheduleOriginRunEvidence>>(StringComparer.Ordinal));

        var integration = new DomainIntegration(
            IntId,
            "My integration",
            "Sample.Mod",
            null,
            IntegrationState.Active,
            null,
            integrationCreatedAt,
            utcNow);
        var schedules = new List<ScheduleDefinition> { HourlyEnabledSchedule() };

        var integrationManagement = new Mock<IIntegrationManagementService>();
        integrationManagement.Setup(x => x.GetIntegrationAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);

        var readinessSvc = new Mock<IIntegrationReadinessService>();
        readinessSvc.Setup(x => x.EvaluateScheduleExpectationAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildScheduleExpectationSnapshotForTest(
                integration,
                RunnableReadiness(),
                schedules,
                utcNow,
                latestScheduleOrigin: null));

        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedules);

        var sut = CreateService(utcNow, runQuery, integrationManagement, readinessSvc, schedRepo);
        var diag = await sut.GetDiagnosticsAsync(IntId);
        diag.Should().NotBeNull();
        diag!.EvaluationStatus.Should().Be(ScheduleExpectationEvaluationStatus.MissingExpectedRun);
        diag.LastSatisfyingScheduleOriginAuthorizedRunId.Should().BeNull();
        diag.RecentExpectedFires[0].ExpectedFireUtc.Should().Be(mostRecentExpectedFire);
        diag.RecentExpectedFires[0].Outcome.Should().Be(ExpectedFireDiagnosticOutcome.Missing);
    }

    [Fact]
    public async Task Diagnostics_when_in_grace_without_run_returns_GraceSuppressed_in_timeline()
    {
        var utcNow = DateTimeOffset.Parse("2025-01-15T12:30:00Z");
        var integrationCreatedAt = utcNow - TimeSpan.FromDays(10);
        var expectedFires = RecentExpectedFires(utcNow);
        expectedFires.Should().NotBeEmpty();
        var mostRecentExpectedFire = expectedFires[0];

        var graceAnchor = utcNow - TimeSpan.FromHours(12);
        var graceAnchorStr = graceAnchor.ToString("O");

        var runQuery = new Mock<IRunQueryService>();
        runQuery.Setup(x => x.GetFleetOperationalRunSignalsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> ids, DateTimeOffset _, int _, CancellationToken _) =>
            {
                var d = new Dictionary<string, FleetOperationalRunSignals>(StringComparer.Ordinal);
                d[ids[0]] = new FleetOperationalRunSignals(0, null, null, null, null, null);
                return d;
            });

        runQuery.Setup(x => x.GetLatestAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, FleetScheduleOriginRunEvidence>(StringComparer.Ordinal));

        runQuery.Setup(x => x.GetAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyList<FleetScheduleOriginRunEvidence>>(StringComparer.Ordinal));

        var integration = new DomainIntegration(
            IntId,
            "My integration",
            "Sample.Mod",
            null,
            IntegrationState.Active,
            null,
            integrationCreatedAt,
            utcNow);
        var schedules = new List<ScheduleDefinition> { HourlyEnabledSchedule(lifecycleEnabledAtUtc: graceAnchorStr) };

        var integrationManagement = new Mock<IIntegrationManagementService>();
        integrationManagement.Setup(x => x.GetIntegrationAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);

        var readinessSvc = new Mock<IIntegrationReadinessService>();
        readinessSvc.Setup(x => x.EvaluateScheduleExpectationAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildScheduleExpectationSnapshotForTest(
                integration,
                RunnableReadiness(),
                schedules,
                utcNow,
                latestScheduleOrigin: null));

        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedules);

        var sut = CreateService(utcNow, runQuery, integrationManagement, readinessSvc, schedRepo);
        var diag = await sut.GetDiagnosticsAsync(IntId);
        diag.Should().NotBeNull();
        diag!.EvaluationStatus.Should().Be(ScheduleExpectationEvaluationStatus.Grace);
        diag.GraceAnchorUtc.Should().Be(graceAnchor);
        diag.LastScheduleEnabledAtUtc.Should().Be(graceAnchor);
        diag.RecentExpectedFires.Should().NotBeEmpty();
        diag.RecentExpectedFires[0].ExpectedFireUtc.Should().Be(mostRecentExpectedFire);
        diag.RecentExpectedFires[0].Outcome.Should().Be(ExpectedFireDiagnosticOutcome.GraceSuppressed);
        diag.RecentExpectedFires[0].SuppressedByGraceReason.Should().Be(ScheduleExpectationGraceReason.RecentlyEnabled);
    }

    [Fact]
    public async Task Diagnostics_when_schedule_disabled_returns_NoExpectation_and_empty_timeline()
    {
        var utcNow = DateTimeOffset.Parse("2025-01-15T12:30:00Z");
        var integrationCreatedAt = utcNow - TimeSpan.FromDays(10);

        var disabledSchedule = new ScheduleDefinition(
            new ScheduleId(ScheduleId),
            "N",
            "0 * * * *",
            "UTC",
            Enabled: false,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            null,
            Version: 1,
            IntegrationId: IntId,
            Lifecycle: new ScheduleLifecycleEvidence(utcNow - TimeSpan.FromDays(3), null, null));

        var runQuery = new Mock<IRunQueryService>();
        runQuery.Setup(x => x.GetFleetOperationalRunSignalsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, FleetOperationalRunSignals>(StringComparer.Ordinal));
        runQuery.Setup(x => x.GetLatestAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, FleetScheduleOriginRunEvidence>(StringComparer.Ordinal));
        runQuery.Setup(x => x.GetAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyList<FleetScheduleOriginRunEvidence>>(StringComparer.Ordinal));

        var integration = new DomainIntegration(
            IntId,
            "My integration",
            "Sample.Mod",
            null,
            IntegrationState.Active,
            null,
            integrationCreatedAt,
            utcNow);
        var schedules = new List<ScheduleDefinition> { disabledSchedule };

        var integrationManagement = new Mock<IIntegrationManagementService>();
        integrationManagement.Setup(x => x.GetIntegrationAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);

        var readinessSvc = new Mock<IIntegrationReadinessService>();
        readinessSvc.Setup(x => x.EvaluateScheduleExpectationAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildScheduleExpectationSnapshotForTest(
                integration,
                RunnableReadiness(),
                schedules,
                utcNow,
                latestScheduleOrigin: null));

        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedules);

        var sut = CreateService(utcNow, runQuery, integrationManagement, readinessSvc, schedRepo);
        var diag = await sut.GetDiagnosticsAsync(IntId);
        diag.Should().NotBeNull();
        diag!.HasRecurringScheduleExpectation.Should().BeFalse();
        diag.EvaluationStatus.Should().Be(ScheduleExpectationEvaluationStatus.NoExpectation);
        diag.ReferenceScheduleEnabled.Should().BeFalse();
        diag.RecentExpectedFires.Should().BeEmpty();
    }

    [Fact]
    public async Task Diagnostics_manual_runs_do_not_satisfy_schedule_expectation()
    {
        var utcNow = DateTimeOffset.Parse("2025-01-15T12:30:00Z");
        var integrationCreatedAt = utcNow - TimeSpan.FromDays(10);

        var expectedFires = RecentExpectedFires(utcNow);
        expectedFires.Should().NotBeEmpty();
        var mostRecentExpectedFire = expectedFires[0];

        var manualLatestRunId = "run-manual-1";
        var manualLatestRunAt = utcNow.AddMinutes(-1);

        var runQuery = new Mock<IRunQueryService>();
        runQuery.Setup(x => x.GetFleetOperationalRunSignalsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> ids, DateTimeOffset _, int _, CancellationToken _) =>
            {
                var d = new Dictionary<string, FleetOperationalRunSignals>(StringComparer.Ordinal)
                {
                    [ids[0]] = new FleetOperationalRunSignals(0, null, null, manualLatestRunId, "completed", manualLatestRunAt)
                };
                return d;
            });

        // No schedule-origin authorized runs.
        runQuery.Setup(x => x.GetLatestAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, FleetScheduleOriginRunEvidence>(StringComparer.Ordinal));

        runQuery.Setup(x => x.GetAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyList<FleetScheduleOriginRunEvidence>>(StringComparer.Ordinal));

        var integration = new DomainIntegration(
            IntId,
            "My integration",
            "Sample.Mod",
            null,
            IntegrationState.Active,
            null,
            integrationCreatedAt,
            utcNow);
        var schedules = new List<ScheduleDefinition> { HourlyEnabledSchedule() };

        var integrationManagement = new Mock<IIntegrationManagementService>();
        integrationManagement.Setup(x => x.GetIntegrationAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);

        var readinessSvc = new Mock<IIntegrationReadinessService>();
        readinessSvc.Setup(x => x.EvaluateScheduleExpectationAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildScheduleExpectationSnapshotForTest(
                integration,
                RunnableReadiness(),
                schedules,
                utcNow,
                latestScheduleOrigin: null));

        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedules);

        var sut = CreateService(utcNow, runQuery, integrationManagement, readinessSvc, schedRepo);
        var diag = await sut.GetDiagnosticsAsync(IntId);
        diag.Should().NotBeNull();
        diag!.LastAuthorizedRunId.Should().Be(manualLatestRunId);
        diag.EvaluationStatus.Should().Be(ScheduleExpectationEvaluationStatus.MissingExpectedRun);
        diag.RecentExpectedFires[0].ExpectedFireUtc.Should().Be(mostRecentExpectedFire);
        diag.RecentExpectedFires[0].Outcome.Should().Be(ExpectedFireDiagnosticOutcome.Missing);
    }

    [Fact]
    public async Task Diagnostics_recent_expected_fires_is_bounded_to_10()
    {
        var utcNow = DateTimeOffset.Parse("2025-01-15T12:30:00Z");
        var integrationCreatedAt = utcNow - TimeSpan.FromDays(10);

        var runQuery = new Mock<IRunQueryService>();
        runQuery.Setup(x => x.GetFleetOperationalRunSignalsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, FleetOperationalRunSignals>(StringComparer.Ordinal));
        runQuery.Setup(x => x.GetLatestAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, FleetScheduleOriginRunEvidence>(StringComparer.Ordinal));
        runQuery.Setup(x => x.GetAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyList<FleetScheduleOriginRunEvidence>>(StringComparer.Ordinal));

        var integration = new DomainIntegration(
            IntId,
            "My integration",
            "Sample.Mod",
            null,
            IntegrationState.Active,
            null,
            integrationCreatedAt,
            utcNow);
        var schedules = new List<ScheduleDefinition> { HourlyEnabledSchedule() };

        var integrationManagement = new Mock<IIntegrationManagementService>();
        integrationManagement.Setup(x => x.GetIntegrationAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);

        var readinessSvc = new Mock<IIntegrationReadinessService>();
        readinessSvc.Setup(x => x.EvaluateScheduleExpectationAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildScheduleExpectationSnapshotForTest(
                integration,
                RunnableReadiness(),
                schedules,
                utcNow,
                latestScheduleOrigin: null));

        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedules);

        var sut = CreateService(utcNow, runQuery, integrationManagement, readinessSvc, schedRepo);
        var diag = await sut.GetDiagnosticsAsync(IntId);
        diag.Should().NotBeNull();
        diag!.RecentExpectedFires.Count.Should().Be(10);
    }
}

