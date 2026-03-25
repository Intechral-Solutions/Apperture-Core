using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Integration;
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
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy());

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
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy());

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
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy());

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
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy());

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

        var sut = new IntegrationReadinessService(mgmt.Object, slots.Object, bindRepo.Object, connRepo.Object, elig.Object, schedRepo.Object, new AdvisoryConnectionVerificationReadinessPolicy());
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
            slots.Object,
            bindRepo.Object,
            connRepo.Object,
            elig.Object,
            schedRepo.Object,
            new AdvisoryConnectionVerificationReadinessPolicy());

        var r = await sut.GetReadinessAsync(IntId);
        r.Should().NotBeNull();
        r!.BlockingIssueCount.Should().BeGreaterThan(0);
        r.AdvisoryIssueCount.Should().Be(0);
        r.Issues.Should().Contain(i => i.Code == IntegrationReadinessIssueCodes.MissingSlotBinding && i.Severity == IntegrationReadinessIssueSeverity.Blocking.ToString());
    }
}
