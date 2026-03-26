using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using Apperture.ModuleSdk.Abstractions;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Integration;

public sealed class IntegrationReadinessServiceTests
{
    private static readonly IntegrationId IntId = new("int01");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2025-01-01T00:00:00Z");

    private sealed class FixedUtcTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    /// <summary>Frozen clock for fleet queries so cron windows are deterministic.</summary>
    private static readonly TimeProvider FleetUtcClock = new FixedUtcTimeProvider(Now);

    private static readonly TimeProvider MidJanuaryFleetClock =
        new FixedUtcTimeProvider(DateTimeOffset.Parse("2025-01-15T12:00:00Z"));

    private static readonly TimeProvider LateJan1FleetClock =
        new FixedUtcTimeProvider(DateTimeOffset.Parse("2025-01-01T18:00:00Z"));

    private static Mock<IRunQueryService> RunQueryAllEmpty()
    {
        var m = new Mock<IRunQueryService>();
        m.Setup(x => x.GetFleetOperationalRunSignalsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> ids, DateTimeOffset _, int _, CancellationToken _) =>
            {
                var d = new Dictionary<string, FleetOperationalRunSignals>(StringComparer.Ordinal);
                foreach (var id in ids.Distinct(StringComparer.Ordinal))
                    d[id] = new FleetOperationalRunSignals(0, null, null, null, null, null);
                return d;
            });
        m.Setup(x => x.GetLatestAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, FleetScheduleOriginRunEvidence>(StringComparer.Ordinal));
        return m;
    }

    private static IIntegrationRepository NullIntegrationRepository()
    {
        var m = new Mock<IIntegrationRepository>();
        m.Setup(x => x.GetByIdAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DomainIntegration?)null);
        return m.Object;
    }

    private static IAttentionIncidentTracker EmptyAttentionIncidentTracker()
    {
        var m = new Mock<IAttentionIncidentTracker>();
        m.Setup(x => x.GetIntegrationIdsWithActiveConditionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.Ordinal));
        m.Setup(x => x.ObserveScheduleExpectationAsync(
                It.IsAny<IntegrationId>(),
                It.IsAny<ScheduleExpectationSnapshot>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttentionIncidentObserveResult(AttentionIncidentObserveTransition.NoChange, null));
        return m.Object;
    }

    private static DomainIntegration ActiveIntegration() =>
        new(IntId, "My integration", "Sample.Mod", null, IntegrationState.Active, null, Now, Now);

    private sealed class TestSlot : ICapabilitySlot
    {
        public required string ModuleId { get; init; }
        public required string SlotName { get; init; }
        public required string ContractType { get; init; }
    }

    [Fact]
    public async Task GetReadiness_happy_path_is_runnable()
    {
        var connId = new ConnectionId("conn1");
        var binding = new Binding(
            new BindingId("bind1"),
            IntId,
            "Sample.Mod",
            "Db",
            "System.Data.IDbConnection",
            connId,
            null,
            BindingState.Active);
        var connection = new Connection(
            connId,
            new ConnectorDefinitionId("sql"),
            "SQL",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            null,
            null,
            ConnectionState.Active);

        var mgmt = new Mock<IIntegrationManagementService>();
        mgmt.Setup(x => x.GetIntegrationAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveIntegration());

        var slots = new Mock<IModuleSlotRegistry>();
        slots.Setup(x => x.GetSlotsForModule("Sample.Mod"))
            .Returns(new List<ICapabilitySlot>
            {
                new TestSlot { ModuleId = "Sample.Mod", SlotName = "Db", ContractType = "System.Data.IDbConnection" }
            });

        var bindRepo = new Mock<IBindingRepository>();
        bindRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Binding> { binding });

        var connRepo = new Mock<IConnectionRepository>();
        connRepo.Setup(x => x.GetByIdAsync(connId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);

        var elig = new Mock<IBindingEligibilityService>();
        elig.Setup(x => x.GetEligibleConnectionsAsync("Sample.Mod", "Db", "System.Data.IDbConnection", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { connection });

        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ScheduleDefinition>());

        var sut = new IntegrationReadinessService(
            mgmt.Object,
            NullIntegrationRepository(),
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy(),
            RunQueryAllEmpty().Object,
            EmptyAttentionIncidentTracker(),
            new ScheduleExpectationEvaluator(),
            FleetUtcClock);

        var r = await sut.GetReadinessAsync(IntId);
        r.Should().NotBeNull();
        r!.IsRunnable.Should().BeTrue();
        r.Issues.Should().ContainSingle(i => i.Code == IntegrationReadinessIssueCodes.ConnectionVerificationUnverified);
    }

    [Fact]
    public async Task GetReadiness_missing_slot_binding_reports_code()
    {
        var mgmt = new Mock<IIntegrationManagementService>();
        mgmt.Setup(x => x.GetIntegrationAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveIntegration());

        var slots = new Mock<IModuleSlotRegistry>();
        slots.Setup(x => x.GetSlotsForModule("Sample.Mod"))
            .Returns(new List<ICapabilitySlot>
            {
                new TestSlot { ModuleId = "Sample.Mod", SlotName = "Db", ContractType = "System.Data.IDbConnection" }
            });

        var bindRepo = new Mock<IBindingRepository>();
        bindRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Binding>());

        var connRepo = new Mock<IConnectionRepository>();
        var elig = new Mock<IBindingEligibilityService>();
        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ScheduleDefinition>());

        var sut = new IntegrationReadinessService(
            mgmt.Object,
            NullIntegrationRepository(),
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy(),
            RunQueryAllEmpty().Object,
            EmptyAttentionIncidentTracker(),
            new ScheduleExpectationEvaluator(),
            FleetUtcClock);

        var r = await sut.GetReadinessAsync(IntId);
        r!.IsRunnable.Should().BeFalse();
        r.Issues.Should().ContainSingle(i => i.Code == IntegrationReadinessIssueCodes.MissingSlotBinding);
    }

    [Fact]
    public async Task GetReadiness_retired_integration_not_runnable()
    {
        var retired = new DomainIntegration(
            IntId,
            "R",
            "Sample.Mod",
            null,
            IntegrationState.Retired,
            null,
            Now,
            Now);

        var mgmt = new Mock<IIntegrationManagementService>();
        mgmt.Setup(x => x.GetIntegrationAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(retired);

        var slots = new Mock<IModuleSlotRegistry>();
        slots.Setup(x => x.GetSlotsForModule("Sample.Mod")).Returns(new List<ICapabilitySlot>());

        var bindRepo = new Mock<IBindingRepository>();
        bindRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Binding>());
        var connRepo = new Mock<IConnectionRepository>();
        var elig = new Mock<IBindingEligibilityService>();
        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ScheduleDefinition>());

        var sut = new IntegrationReadinessService(
            mgmt.Object,
            NullIntegrationRepository(),
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy(),
            RunQueryAllEmpty().Object,
            EmptyAttentionIncidentTracker(),
            new ScheduleExpectationEvaluator(),
            FleetUtcClock);

        var r = await sut.GetReadinessAsync(IntId);
        r!.IsRunnable.Should().BeFalse();
        r.Issues.Should().Contain(i => i.Code == IntegrationReadinessIssueCodes.IntegrationNotActive);
    }

    [Fact]
    public async Task GetReadiness_connection_not_eligible_reports_binding_not_eligible()
    {
        var connId = new ConnectionId("conn1");
        var binding = new Binding(
            new BindingId("bind1"),
            IntId,
            "Sample.Mod",
            "Db",
            "System.Data.IDbConnection",
            connId,
            null,
            BindingState.Active);
        var connection = new Connection(
            connId,
            new ConnectorDefinitionId("sql"),
            "SQL",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            null,
            null,
            ConnectionState.Active);

        var mgmt = new Mock<IIntegrationManagementService>();
        mgmt.Setup(x => x.GetIntegrationAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveIntegration());

        var slots = new Mock<IModuleSlotRegistry>();
        slots.Setup(x => x.GetSlotsForModule("Sample.Mod"))
            .Returns(new List<ICapabilitySlot>
            {
                new TestSlot { ModuleId = "Sample.Mod", SlotName = "Db", ContractType = "System.Data.IDbConnection" }
            });

        var bindRepo = new Mock<IBindingRepository>();
        bindRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Binding> { binding });

        var connRepo = new Mock<IConnectionRepository>();
        connRepo.Setup(x => x.GetByIdAsync(connId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);

        var elig = new Mock<IBindingEligibilityService>();
        elig.Setup(x => x.GetEligibleConnectionsAsync("Sample.Mod", "Db", "System.Data.IDbConnection", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection>());

        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ScheduleDefinition>());

        var sut = new IntegrationReadinessService(
            mgmt.Object,
            NullIntegrationRepository(),
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy(),
            RunQueryAllEmpty().Object,
            EmptyAttentionIncidentTracker(),
            new ScheduleExpectationEvaluator(),
            FleetUtcClock);

        var r = await sut.GetReadinessAsync(IntId);
        r!.IsRunnable.Should().BeFalse();
        r.Issues.Should().ContainSingle(i => i.Code == IntegrationReadinessIssueCodes.BindingNotEligible);
    }

    [Fact]
    public async Task GetReadiness_failed_verification_is_advisory_not_blocking()
    {
        var connId = new ConnectionId("connv1");
        var binding = new Binding(new BindingId("bindv1"), IntId, "Sample.Mod", "Db", "System.Data.IDbConnection", connId, null, BindingState.Active);
        var connection = new Connection(
            connId,
            new ConnectorDefinitionId("sql"),
            "SQL",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            null,
            null,
            ConnectionState.Active,
            ConnectionVerificationStatus.Failed,
            Now,
            "Auth failed");

        var mgmt = new Mock<IIntegrationManagementService>();
        mgmt.Setup(x => x.GetIntegrationAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveIntegration());
        var slots = new Mock<IModuleSlotRegistry>();
        slots.Setup(x => x.GetSlotsForModule("Sample.Mod")).Returns(new List<ICapabilitySlot> { new TestSlot { ModuleId = "Sample.Mod", SlotName = "Db", ContractType = "System.Data.IDbConnection" } });
        var bindRepo = new Mock<IBindingRepository>();
        bindRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Binding> { binding });
        var connRepo = new Mock<IConnectionRepository>();
        connRepo.Setup(x => x.GetByIdAsync(connId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        var elig = new Mock<IBindingEligibilityService>();
        elig.Setup(x => x.GetEligibleConnectionsAsync("Sample.Mod", "Db", "System.Data.IDbConnection", It.IsAny<CancellationToken>())).ReturnsAsync(new List<Connection> { connection });
        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ScheduleDefinition>());

        var sut = new IntegrationReadinessService(mgmt.Object, NullIntegrationRepository(), slots.Object, bindRepo.Object, connRepo.Object, elig.Object, schedRepo.Object, new AdvisoryConnectionVerificationReadinessPolicy(), RunQueryAllEmpty().Object, EmptyAttentionIncidentTracker(), new ScheduleExpectationEvaluator(), FleetUtcClock);
        var r = await sut.GetReadinessAsync(IntId);
        r!.IsRunnable.Should().BeTrue();
        r.Issues.Should().ContainSingle(i =>
            i.Code == IntegrationReadinessIssueCodes.ConnectionVerificationFailed
            && i.Severity == IntegrationReadinessIssueSeverity.Advisory.ToString());
    }

    [Fact]
    public async Task GetReadiness_reports_explicit_blocking_and_advisory_counts()
    {
        var mgmt = new Mock<IIntegrationManagementService>();
        mgmt.Setup(x => x.GetIntegrationAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveIntegration());

        var slots = new Mock<IModuleSlotRegistry>();
        slots.Setup(x => x.GetSlotsForModule("Sample.Mod"))
            .Returns(new List<ICapabilitySlot>
            {
                new TestSlot { ModuleId = "Sample.Mod", SlotName = "Db", ContractType = "System.Data.IDbConnection" }
            });

        var bindRepo = new Mock<IBindingRepository>();
        bindRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Binding>());

        var connRepo = new Mock<IConnectionRepository>();
        var elig = new Mock<IBindingEligibilityService>();
        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ScheduleDefinition>());

        var sut = new IntegrationReadinessService(
            mgmt.Object,
            NullIntegrationRepository(),
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy(),
            RunQueryAllEmpty().Object,
            EmptyAttentionIncidentTracker(),
            new ScheduleExpectationEvaluator(),
            FleetUtcClock);

        var r = await sut.GetReadinessAsync(IntId);
        r.Should().NotBeNull();
        r!.BlockingIssueCount.Should().BeGreaterThan(0);
        r.AdvisoryIssueCount.Should().Be(0);
        r.Issues.Should().Contain(i => i.Code == IntegrationReadinessIssueCodes.MissingSlotBinding && i.Severity == IntegrationReadinessIssueSeverity.Blocking.ToString());
    }

    [Fact]
    public async Task ListFleet_inactive_row_has_no_operational_attention_tokens()
    {
        var inactive = new DomainIntegration(
            IntId,
            "Paused",
            "Sample.Mod",
            null,
            IntegrationState.Inactive,
            null,
            Now,
            Now);

        var mgmt = new Mock<IIntegrationManagementService>();
        mgmt.Setup(x => x.ListIntegrationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<DomainIntegration> { inactive });

        var slots = new Mock<IModuleSlotRegistry>();
        slots.Setup(x => x.GetSlotsForModule("Sample.Mod")).Returns(new List<ICapabilitySlot>());
        var bindRepo = new Mock<IBindingRepository>();
        bindRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Binding>());
        var connRepo = new Mock<IConnectionRepository>();
        var elig = new Mock<IBindingEligibilityService>();
        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ScheduleDefinition>());

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

        var sut = new IntegrationReadinessService(
            mgmt.Object,
            NullIntegrationRepository(),
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy(),
            runQuery.Object,
            EmptyAttentionIncidentTracker(),
            new ScheduleExpectationEvaluator(),
            FleetUtcClock);

        var fleet = await sut.ListFleetAsync(new IntegrationFleetQuery());
        var row = fleet.Should().ContainSingle().Subject;
        row.OperationalRuns.EvidenceWindowStartUtc.Should().BeNull();
        row.OperationalRuns.HasRecentOperationalFailure.Should().BeFalse();
        row.AttentionTokens.Should().NotContain(IntegrationFleetAttention.Tokens.OperationalFailure);
        row.AttentionTokens.Should().NotContain(IntegrationFleetAttention.Tokens.RepeatedOperationalFailure);
        row.ExpectedRuns.HasUnexpectedSilence.Should().BeFalse();
        row.AttentionTokens.Should().NotContain(IntegrationFleetAttention.Tokens.ExpectedRunMissing);
        runQuery.Verify(x => x.GetFleetOperationalRunSignalsAsync(
            It.Is<IReadOnlyList<string>>(ids => ids.Count == 0),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListFleet_active_with_run_signals_gets_operational_tokens_and_filter()
    {
        var mgmt = new Mock<IIntegrationManagementService>();
        mgmt.Setup(x => x.ListIntegrationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<DomainIntegration> { ActiveIntegration() });

        var slots = new Mock<IModuleSlotRegistry>();
        slots.Setup(x => x.GetSlotsForModule("Sample.Mod"))
            .Returns(new List<ICapabilitySlot>
            {
                new TestSlot { ModuleId = "Sample.Mod", SlotName = "Db", ContractType = "System.Data.IDbConnection" }
            });
        var bindRepo = new Mock<IBindingRepository>();
        bindRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Binding>());
        var connRepo = new Mock<IConnectionRepository>();
        var elig = new Mock<IBindingEligibilityService>();
        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ScheduleDefinition>());

        var failedAt = DateTimeOffset.Parse("2025-01-02T12:00:00Z");
        var runQuery = new Mock<IRunQueryService>();
        runQuery.Setup(x => x.GetFleetOperationalRunSignalsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> ids, DateTimeOffset _, int _, CancellationToken _) =>
            {
                var d = new Dictionary<string, FleetOperationalRunSignals>(StringComparer.Ordinal);
                foreach (var id in ids.Distinct(StringComparer.Ordinal))
                {
                    d[id] = id == "int01"
                        ? new FleetOperationalRunSignals(2, "run-fail-1", failedAt, "run-fail-1", "failed", failedAt)
                        : new FleetOperationalRunSignals(0, null, null, null, null, null);
                }

                return d;
            });
        runQuery.Setup(x => x.GetLatestAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, FleetScheduleOriginRunEvidence>(StringComparer.Ordinal));

        var sut = new IntegrationReadinessService(
            mgmt.Object,
            NullIntegrationRepository(),
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy(),
            runQuery.Object,
            EmptyAttentionIncidentTracker(),
            new ScheduleExpectationEvaluator(),
            FleetUtcClock);

        var fleet = await sut.ListFleetAsync(new IntegrationFleetQuery());
        var row = fleet.Should().ContainSingle().Subject;
        row.OperationalRuns.RecentFailedRunCount.Should().Be(2);
        row.OperationalRuns.HasRecentOperationalFailure.Should().BeTrue();
        row.OperationalRuns.HasRepeatedRecentFailures.Should().BeTrue();
        row.OperationalRuns.MostRecentAuthorizedRunFailed.Should().BeTrue();
        row.AttentionTokens.Should().Contain(IntegrationFleetAttention.Tokens.OperationalFailure);
        row.AttentionTokens.Should().Contain(IntegrationFleetAttention.Tokens.RepeatedOperationalFailure);
        row.ExpectedRuns.HasUnexpectedSilence.Should().BeFalse();
        row.AttentionTokens.Should().NotContain(IntegrationFleetAttention.Tokens.ExpectedRunMissing);

        var filteredOut = await sut.ListFleetAsync(new IntegrationFleetQuery(HasOperationalFailure: false));
        filteredOut.Should().BeEmpty();

        var filteredIn = await sut.ListFleetAsync(new IntegrationFleetQuery(HasOperationalFailure: true));
        filteredIn.Should().ContainSingle();
    }

    [Fact]
    public async Task ListFleet_active_runnable_scheduled_no_run_gets_expected_run_missing_and_filter()
    {
        var createdOld = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        var integration = new DomainIntegration(
            IntId,
            "My integration",
            "Sample.Mod",
            null,
            IntegrationState.Active,
            null,
            createdOld,
            Now);

        var connId = new ConnectionId("conn1");
        var binding = new Binding(
            new BindingId("bind1"),
            IntId,
            "Sample.Mod",
            "Db",
            "System.Data.IDbConnection",
            connId,
            null,
            BindingState.Active);
        var connection = new Connection(
            connId,
            new ConnectorDefinitionId("sql"),
            "SQL",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            null,
            null,
            ConnectionState.Active);

        var schedule = new ScheduleDefinition(
            new ScheduleId("sch-fleet-63"),
            "Daily",
            "0 0 * * *",
            "UTC",
            true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            null,
            1,
            IntId);

        var mgmt = new Mock<IIntegrationManagementService>();
        mgmt.Setup(x => x.ListIntegrationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<DomainIntegration> { integration });

        var slots = new Mock<IModuleSlotRegistry>();
        slots.Setup(x => x.GetSlotsForModule("Sample.Mod"))
            .Returns(new List<ICapabilitySlot>
            {
                new TestSlot { ModuleId = "Sample.Mod", SlotName = "Db", ContractType = "System.Data.IDbConnection" }
            });

        var bindRepo = new Mock<IBindingRepository>();
        bindRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Binding> { binding });

        var connRepo = new Mock<IConnectionRepository>();
        connRepo.Setup(x => x.GetByIdAsync(connId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);

        var elig = new Mock<IBindingEligibilityService>();
        elig.Setup(x => x.GetEligibleConnectionsAsync("Sample.Mod", "Db", "System.Data.IDbConnection", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { connection });

        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ScheduleDefinition> { schedule });

        var runQuery = RunQueryAllEmpty();

        var sut = new IntegrationReadinessService(
            mgmt.Object,
            NullIntegrationRepository(),
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy(),
            runQuery.Object,
            EmptyAttentionIncidentTracker(),
            new ScheduleExpectationEvaluator(),
            FleetUtcClock);

        var fleet = await sut.ListFleetAsync(new IntegrationFleetQuery());
        var row = fleet.Should().ContainSingle().Subject;
        row.Readiness.IsRunnable.Should().BeTrue();
        row.ExpectedRuns.HasRecurringScheduleExpectation.Should().BeTrue();
        row.ExpectedRuns.HasUnexpectedSilence.Should().BeTrue();
        row.ExpectedRuns.ReferenceScheduleId.Should().Be("sch-fleet-63");
        row.AttentionTokens.Should().Contain(IntegrationFleetAttention.Tokens.ExpectedRunMissing);

        var silentOnly = await sut.ListFleetAsync(new IntegrationFleetQuery(HasUnexpectedSilence: true));
        silentOnly.Should().ContainSingle();

        var notSilent = await sut.ListFleetAsync(new IntegrationFleetQuery(HasUnexpectedSilence: false));
        notSilent.Should().BeEmpty();
    }

    [Fact]
    public async Task ListFleet_active_scheduled_manual_authorized_run_still_flags_expected_run_missing()
    {
        var createdOld = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        var integration = new DomainIntegration(
            IntId,
            "My integration",
            "Sample.Mod",
            null,
            IntegrationState.Active,
            null,
            createdOld,
            Now);

        var connId = new ConnectionId("conn1");
        var binding = new Binding(
            new BindingId("bind1"),
            IntId,
            "Sample.Mod",
            "Db",
            "System.Data.IDbConnection",
            connId,
            null,
            BindingState.Active);
        var connection = new Connection(
            connId,
            new ConnectorDefinitionId("sql"),
            "SQL",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            null,
            null,
            ConnectionState.Active);

        var schedule = new ScheduleDefinition(
            new ScheduleId("sch-fleet-64"),
            "Daily",
            "0 0 * * *",
            "UTC",
            true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            null,
            1,
            IntId);

        var mgmt = new Mock<IIntegrationManagementService>();
        mgmt.Setup(x => x.ListIntegrationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<DomainIntegration> { integration });

        var slots = new Mock<IModuleSlotRegistry>();
        slots.Setup(x => x.GetSlotsForModule("Sample.Mod"))
            .Returns(new List<ICapabilitySlot>
            {
                new TestSlot { ModuleId = "Sample.Mod", SlotName = "Db", ContractType = "System.Data.IDbConnection" }
            });

        var bindRepo = new Mock<IBindingRepository>();
        bindRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Binding> { binding });

        var connRepo = new Mock<IConnectionRepository>();
        connRepo.Setup(x => x.GetByIdAsync(connId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);

        var elig = new Mock<IBindingEligibilityService>();
        elig.Setup(x => x.GetEligibleConnectionsAsync("Sample.Mod", "Db", "System.Data.IDbConnection", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { connection });

        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ScheduleDefinition> { schedule });

        var manualAt = DateTimeOffset.Parse("2025-01-02T12:00:00Z");
        var runQuery = new Mock<IRunQueryService>();
        runQuery.Setup(x => x.GetFleetOperationalRunSignalsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> ids, DateTimeOffset _, int _, CancellationToken _) =>
            {
                var d = new Dictionary<string, FleetOperationalRunSignals>(StringComparer.Ordinal);
                foreach (var id in ids.Distinct(StringComparer.Ordinal))
                {
                    d[id] = id == "int01"
                        ? new FleetOperationalRunSignals(0, null, null, "run-manual", "completed", manualAt)
                        : new FleetOperationalRunSignals(0, null, null, null, null, null);
                }

                return d;
            });
        runQuery.Setup(x => x.GetLatestAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, FleetScheduleOriginRunEvidence>(StringComparer.Ordinal));

        var sut = new IntegrationReadinessService(
            mgmt.Object,
            NullIntegrationRepository(),
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy(),
            runQuery.Object,
            EmptyAttentionIncidentTracker(),
            new ScheduleExpectationEvaluator(),
            FleetUtcClock);

        var fleet = await sut.ListFleetAsync(new IntegrationFleetQuery());
        var row = fleet.Should().ContainSingle().Subject;
        row.OperationalRuns.LastAuthorizedRunAt.Should().Be(manualAt);
        row.ExpectedRuns.HasUnexpectedSilence.Should().BeTrue();
        row.AttentionTokens.Should().Contain(IntegrationFleetAttention.Tokens.ExpectedRunMissing);
    }

    [Fact]
    public async Task ListFleet_monthly_cron_no_expected_fire_in_window_is_not_flagged_silent()
    {
        var createdOld = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        var integration = new DomainIntegration(
            IntId,
            "My integration",
            "Sample.Mod",
            null,
            IntegrationState.Active,
            null,
            createdOld,
            DateTimeOffset.Parse("2025-01-15T12:00:00Z"));

        var connId = new ConnectionId("conn1");
        var binding = new Binding(
            new BindingId("bind1"),
            IntId,
            "Sample.Mod",
            "Db",
            "System.Data.IDbConnection",
            connId,
            null,
            BindingState.Active);
        var connection = new Connection(
            connId,
            new ConnectorDefinitionId("sql"),
            "SQL",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            null,
            null,
            ConnectionState.Active);

        var schedule = new ScheduleDefinition(
            new ScheduleId("sch-monthly"),
            "Monthly",
            "0 0 1 * *",
            "UTC",
            true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            null,
            1,
            IntId);

        var mgmt = new Mock<IIntegrationManagementService>();
        mgmt.Setup(x => x.ListIntegrationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<DomainIntegration> { integration });

        var slots = new Mock<IModuleSlotRegistry>();
        slots.Setup(x => x.GetSlotsForModule("Sample.Mod"))
            .Returns(new List<ICapabilitySlot>
            {
                new TestSlot { ModuleId = "Sample.Mod", SlotName = "Db", ContractType = "System.Data.IDbConnection" }
            });

        var bindRepo = new Mock<IBindingRepository>();
        bindRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Binding> { binding });

        var connRepo = new Mock<IConnectionRepository>();
        connRepo.Setup(x => x.GetByIdAsync(connId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);

        var elig = new Mock<IBindingEligibilityService>();
        elig.Setup(x => x.GetEligibleConnectionsAsync("Sample.Mod", "Db", "System.Data.IDbConnection", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { connection });

        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ScheduleDefinition> { schedule });

        var runQuery = RunQueryAllEmpty();

        var sut = new IntegrationReadinessService(
            mgmt.Object,
            NullIntegrationRepository(),
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy(),
            runQuery.Object,
            EmptyAttentionIncidentTracker(),
            new ScheduleExpectationEvaluator(),
            MidJanuaryFleetClock);

        var fleet = await sut.ListFleetAsync(new IntegrationFleetQuery());
        var row = fleet.Should().ContainSingle().Subject;
        row.ExpectedRuns.HasRecurringScheduleExpectation.Should().BeTrue();
        row.ExpectedRuns.HasUnexpectedSilence.Should().BeFalse();
        row.ExpectedRuns.ExpectedRunEvaluationStatus.Should().Be("NoExpectation");
        row.AttentionTokens.Should().NotContain(IntegrationFleetAttention.Tokens.ExpectedRunMissing);
    }

    [Fact]
    public async Task ListFleet_schedule_origin_after_last_expected_daily_fire_is_not_silent()
    {
        var createdOld = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        var integration = new DomainIntegration(
            IntId,
            "My integration",
            "Sample.Mod",
            null,
            IntegrationState.Active,
            null,
            createdOld,
            DateTimeOffset.Parse("2025-01-01T18:00:00Z"));

        var connId = new ConnectionId("conn1");
        var binding = new Binding(
            new BindingId("bind1"),
            IntId,
            "Sample.Mod",
            "Db",
            "System.Data.IDbConnection",
            connId,
            null,
            BindingState.Active);
        var connection = new Connection(
            connId,
            new ConnectorDefinitionId("sql"),
            "SQL",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            null,
            null,
            ConnectionState.Active);

        var schedule = new ScheduleDefinition(
            new ScheduleId("sch-daily"),
            "Daily",
            "0 0 * * *",
            "UTC",
            true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            null,
            1,
            IntId);

        var mgmt = new Mock<IIntegrationManagementService>();
        mgmt.Setup(x => x.ListIntegrationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<DomainIntegration> { integration });

        var slots = new Mock<IModuleSlotRegistry>();
        slots.Setup(x => x.GetSlotsForModule("Sample.Mod"))
            .Returns(new List<ICapabilitySlot>
            {
                new TestSlot { ModuleId = "Sample.Mod", SlotName = "Db", ContractType = "System.Data.IDbConnection" }
            });

        var bindRepo = new Mock<IBindingRepository>();
        bindRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Binding> { binding });

        var connRepo = new Mock<IConnectionRepository>();
        connRepo.Setup(x => x.GetByIdAsync(connId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);

        var elig = new Mock<IBindingEligibilityService>();
        elig.Setup(x => x.GetEligibleConnectionsAsync("Sample.Mod", "Db", "System.Data.IDbConnection", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { connection });

        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ScheduleDefinition> { schedule });

        var runQuery = new Mock<IRunQueryService>();
        runQuery.Setup(x => x.GetFleetOperationalRunSignalsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> ids, DateTimeOffset _, int _, CancellationToken _) =>
            {
                var d = new Dictionary<string, FleetOperationalRunSignals>(StringComparer.Ordinal);
                foreach (var id in ids.Distinct(StringComparer.Ordinal))
                    d[id] = new FleetOperationalRunSignals(0, null, null, null, null, null);
                return d;
            });
        runQuery.Setup(x => x.GetLatestAuthorizedScheduleOriginRunsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, FleetScheduleOriginRunEvidence>(StringComparer.Ordinal)
            {
                ["int01"] = new FleetScheduleOriginRunEvidence(
                    "run-so-1",
                    DateTimeOffset.Parse("2025-01-01T06:00:00Z"))
            });

        var sut = new IntegrationReadinessService(
            mgmt.Object,
            NullIntegrationRepository(),
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy(),
            runQuery.Object,
            EmptyAttentionIncidentTracker(),
            new ScheduleExpectationEvaluator(),
            LateJan1FleetClock);

        var fleet = await sut.ListFleetAsync(new IntegrationFleetQuery());
        var row = fleet.Should().ContainSingle().Subject;
        row.ExpectedRuns.HasUnexpectedSilence.Should().BeFalse();
        row.ExpectedRuns.ExpectedRunSatisfied.Should().BeTrue();
        row.ExpectedRuns.ExpectedRunEvaluationStatus.Should().Be("Satisfied");
        row.AttentionTokens.Should().NotContain(IntegrationFleetAttention.Tokens.ExpectedRunMissing);
    }

    [Fact]
    public async Task ListFleet_recent_schedule_lifecycle_grace_suppresses_expected_run_missing()
    {
        var createdOld = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        var integration = new DomainIntegration(
            IntId,
            "My integration",
            "Sample.Mod",
            null,
            IntegrationState.Active,
            null,
            createdOld,
            Now);

        var connId = new ConnectionId("conn1");
        var binding = new Binding(
            new BindingId("bind1"),
            IntId,
            "Sample.Mod",
            "Db",
            "System.Data.IDbConnection",
            connId,
            null,
            BindingState.Active);
        var connection = new Connection(
            connId,
            new ConnectorDefinitionId("sql"),
            "SQL",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            null,
            null,
            ConnectionState.Active);

        var anchor = DateTimeOffset.Parse("2024-12-31T12:00:00Z");
        var schedule = new ScheduleDefinition(
            new ScheduleId("sch-fleet-grace"),
            "Daily",
            "0 0 * * *",
            "UTC",
            true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            null,
            1,
            IntId,
            new ScheduleLifecycleEvidence(anchor, null, anchor));

        var mgmt = new Mock<IIntegrationManagementService>();
        mgmt.Setup(x => x.ListIntegrationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<DomainIntegration> { integration });

        var slots = new Mock<IModuleSlotRegistry>();
        slots.Setup(x => x.GetSlotsForModule("Sample.Mod"))
            .Returns(new List<ICapabilitySlot>
            {
                new TestSlot { ModuleId = "Sample.Mod", SlotName = "Db", ContractType = "System.Data.IDbConnection" }
            });

        var bindRepo = new Mock<IBindingRepository>();
        bindRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Binding> { binding });

        var connRepo = new Mock<IConnectionRepository>();
        connRepo.Setup(x => x.GetByIdAsync(connId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);

        var elig = new Mock<IBindingEligibilityService>();
        elig.Setup(x => x.GetEligibleConnectionsAsync("Sample.Mod", "Db", "System.Data.IDbConnection", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { connection });

        var schedRepo = new Mock<IScheduleRepository>();
        schedRepo.Setup(x => x.GetByIntegrationIdAsync(IntId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ScheduleDefinition> { schedule });

        var runQuery = RunQueryAllEmpty();

        var sut = new IntegrationReadinessService(
            mgmt.Object,
            NullIntegrationRepository(),
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy(),
            runQuery.Object,
            EmptyAttentionIncidentTracker(),
            new ScheduleExpectationEvaluator(),
            FleetUtcClock);

        var fleet = await sut.ListFleetAsync(new IntegrationFleetQuery());
        var row = fleet.Should().ContainSingle().Subject;
        row.ExpectedRuns.HasUnexpectedSilence.Should().BeFalse();
        row.ExpectedRuns.ExpectedRunInGracePeriod.Should().BeTrue();
        row.ExpectedRuns.ExpectedRunGraceReason.Should().NotBeNullOrWhiteSpace();
        row.AttentionTokens.Should().NotContain(IntegrationFleetAttention.Tokens.ExpectedRunMissing);
    }
}
