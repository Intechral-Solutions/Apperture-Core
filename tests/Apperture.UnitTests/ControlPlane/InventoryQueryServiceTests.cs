using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.Audit;
using Apperture.ControlPlane.Security.Models;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Domain.Scheduling;
using Apperture.ModuleSdk.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.ControlPlane;

public sealed class InventoryQueryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Resources_mode_aggregates_authorized_types()
    {
        var f = Fixture.AllSources();
        var result = await f.Sut.QueryAsync(new InventoryQuery(InventoryModes.Resources), CancellationToken.None);

        result.Items.Select(i => i.Type).Should().BeEquivalentTo(
        [
            InventoryTypeTokens.Module,
            InventoryTypeTokens.Integration,
            InventoryTypeTokens.Connection,
            InventoryTypeTokens.Schedule,
            InventoryTypeTokens.NotificationDefinition
        ]);
        result.AvailableTypes.Should().Equal(
            InventoryTypeTokens.Connection,
            InventoryTypeTokens.Integration,
            InventoryTypeTokens.Module,
            InventoryTypeTokens.NotificationDefinition,
            InventoryTypeTokens.Schedule);
        result.Items.Should().OnlyContain(i => i.Mode == InventoryItemModes.Resource);
        result.Items.Single(i => i.Type == InventoryTypeTokens.Module).Href.Should().Be("/modules/hello");
        result.Items.Single(i => i.Type == InventoryTypeTokens.Integration).Href.Should().Be("/integrations/int-1");
        result.Items.Single(i => i.Type == InventoryTypeTokens.Connection).Href.Should().Be("/connections/conn-1");
        result.Items.Single(i => i.Type == InventoryTypeTokens.Schedule).Href.Should().Be("/schedules/sched-1");
        result.Items.Single(i => i.Type == InventoryTypeTokens.NotificationDefinition).Href
            .Should().Be("/config/notifications/definitions/ndef-1");
    }

    [Fact]
    public async Task Activity_mode_includes_runs_and_audit()
    {
        var f = Fixture.AllSources();
        var result = await f.Sut.QueryAsync(new InventoryQuery(InventoryModes.Activity), CancellationToken.None);

        result.Items.Select(i => i.Type).Should().BeEquivalentTo(
            [InventoryTypeTokens.Run, InventoryTypeTokens.AuditEvent]);
        result.Items.Should().OnlyContain(i => i.Mode == InventoryItemModes.Activity);
        result.Items.Single(i => i.Type == InventoryTypeTokens.Run).Href.Should().Be("/runs/run-1");
        result.Items.Single(i => i.Type == InventoryTypeTokens.AuditEvent).Href
            .Should().Be("/control/audit?runId=run-1");
        f.Modules.Verify(x => x.ListModulesAsync(It.IsAny<CancellationToken>()), Times.Never);
        f.Audit.Verify(x => x.QueryAsync(
            It.Is<AuditFilterCriteria>(c =>
                c.FromUtc == Now.Subtract(InventoryQueryService.AuditWindow)
                && c.PageSize == InventoryQueryService.SourceTake),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Search_matches_name_id_or_type_label()
    {
        var f = Fixture.AllSources();

        var byName = await f.Sut.QueryAsync(new InventoryQuery(Q: "billing"), CancellationToken.None);
        byName.Items.Should().ContainSingle(i => i.Id == "int-1");

        var byId = await f.Sut.QueryAsync(new InventoryQuery(Q: "CONN-1"), CancellationToken.None);
        byId.Items.Should().ContainSingle(i => i.Id == "conn-1");

        var byTypeLabel = await f.Sut.QueryAsync(new InventoryQuery(Q: "notification definition"), CancellationToken.None);
        byTypeLabel.Items.Should().ContainSingle(i => i.Type == InventoryTypeTokens.NotificationDefinition);
    }

    [Fact]
    public async Task Type_filter_is_exact_token()
    {
        var f = Fixture.AllSources();
        var result = await f.Sut.QueryAsync(
            new InventoryQuery(Type: InventoryTypeTokens.Module),
            CancellationToken.None);

        result.Items.Should().ContainSingle(i => i.Type == InventoryTypeTokens.Module);
        result.AvailableTypes.Should().Contain(InventoryTypeTokens.Integration);
    }

    [Fact]
    public async Task Status_filter_is_case_insensitive_contains()
    {
        var f = Fixture.AllSources();
        var result = await f.Sut.QueryAsync(new InventoryQuery(Status: "act"), CancellationToken.None);

        result.Items.Select(i => i.Id).Should().BeEquivalentTo(["int-1", "conn-1"]);
    }

    [Fact]
    public async Task Sort_name_status_and_updated()
    {
        var caps = Fixture.AllowAll();
        var newer = Now;
        var older = Now.AddHours(-2);
        var integrations = new Mock<IIntegrationManagementService>();
        integrations.Setup(x => x.ListIntegrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainIntegration>
            {
                new(new IntegrationId("z"), "Zebra", "m", null, IntegrationState.Retired, null, older, older),
                new(new IntegrationId("a"), "Alpha", "m", null, IntegrationState.Active, null, newer, newer)
            });

        var sut = Fixture.CreateSut(caps, integrations: integrations);

        var byName = await sut.QueryAsync(new InventoryQuery(Sort: InventorySorts.Name), CancellationToken.None);
        byName.Items.Select(i => i.Name).Should().Equal("Alpha", "Zebra");

        var byStatus = await sut.QueryAsync(new InventoryQuery(Sort: InventorySorts.Status), CancellationToken.None);
        byStatus.Items.Select(i => i.Status).Should().Equal("Active", "Retired");

        var byUpdated = await sut.QueryAsync(new InventoryQuery(Sort: InventorySorts.Updated), CancellationToken.None);
        byUpdated.Items.Select(i => i.Id).Should().Equal("a", "z");
    }

    [Fact]
    public async Task Default_sort_is_updated_descending_then_name()
    {
        var caps = Fixture.AllowAll();
        var same = Now;
        var integrations = new Mock<IIntegrationManagementService>();
        integrations.Setup(x => x.ListIntegrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainIntegration>
            {
                new(new IntegrationId("b"), "Beta", "m", null, IntegrationState.Active, null, same, same),
                new(new IntegrationId("a"), "Alpha", "m", null, IntegrationState.Active, null, same, same)
            });

        var sut = Fixture.CreateSut(caps, integrations: integrations);
        var result = await sut.QueryAsync(new InventoryQuery(), CancellationToken.None);

        result.Items.Select(i => i.Name).Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public async Task Missing_capability_omits_type_without_calling_list()
    {
        var caps = Fixture.AllowAll() with { CanViewIntegrations = false };
        var f = Fixture.AllSources(caps);

        var result = await f.Sut.QueryAsync(new InventoryQuery(), CancellationToken.None);

        result.Items.Should().NotContain(i => i.Type == InventoryTypeTokens.Integration);
        result.Items.Should().Contain(i => i.Type == InventoryTypeTokens.Module);
        f.Integrations.Verify(x => x.ListIntegrationsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unauthorized_list_omits_that_type_only()
    {
        var f = Fixture.AllSources();
        f.Connections
            .Setup(x => x.ListConnectionsAsync(It.IsAny<CancellationToken>()))
            .Throws(new UnauthorizedAccessException("denied"));

        var result = await f.Sut.QueryAsync(new InventoryQuery(), CancellationToken.None);

        result.Items.Should().NotContain(i => i.Type == InventoryTypeTokens.Connection);
        result.Items.Should().Contain(i => i.Type == InventoryTypeTokens.Integration);
    }

    [Fact]
    public async Task Audit_without_run_reference_links_to_timeline()
    {
        var f = Fixture.AllSources();
        f.Audit.Setup(x => x.QueryAsync(It.IsAny<AuditFilterCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditTimelinePage(
            [
                new AuditTimelineEntry(
                    "aud-2",
                    Now,
                    "user:a",
                    "auth.sign_in",
                    "auth",
                    "success",
                    "Signed in",
                    null,
                    [])
            ], null));

        var result = await f.Sut.QueryAsync(new InventoryQuery(InventoryModes.Activity), CancellationToken.None);
        result.Items.Single(i => i.Type == InventoryTypeTokens.AuditEvent).Href.Should().Be("/control/audit");
    }

    [Fact]
    public async Task Take_limits_after_sort()
    {
        var caps = Fixture.AllowAll();
        var integrations = new Mock<IIntegrationManagementService>();
        integrations.Setup(x => x.ListIntegrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(1, 5).Select(i =>
                new DomainIntegration(
                    new IntegrationId($"i{i}"),
                    $"Item {i}",
                    "m",
                    null,
                    IntegrationState.Active,
                    null,
                    Now.AddMinutes(i),
                    Now.AddMinutes(i))).ToList());

        var sut = Fixture.CreateSut(caps, integrations: integrations);
        var result = await sut.QueryAsync(new InventoryQuery(Take: 2), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.Items.Select(i => i.Id).Should().Equal("i5", "i4");
    }

    [Fact]
    public async Task Module_surfaces_its_real_installation_state_timestamp()
    {
        var caps = Fixture.AllowAll();
        var updatedAt = Now.AddDays(-3);
        var modules = new Mock<IModuleRegistryService>();
        modules.Setup(x => x.ListModulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ModuleCatalogEntry>
            {
                new(
                    new ModuleDescriptor("hello", "Hello World", "1.0", null, null, null, []),
                    ModuleLifecycleState.InstalledEnabled,
                    Installed: true,
                    Enabled: true,
                    EffectivelyActive: true,
                    ValidationIssues: [],
                    UpdatedAtUtc: updatedAt)
            });

        var sut = Fixture.CreateSut(caps, modules: modules);
        var result = await sut.QueryAsync(new InventoryQuery(), CancellationToken.None);

        result.Items.Single(i => i.Type == InventoryTypeTokens.Module).UpdatedAtUtc.Should().Be(updatedAt);
    }

    [Fact]
    public async Task Discovered_but_never_installed_module_has_no_fabricated_timestamp()
    {
        var caps = Fixture.AllowAll();
        var modules = new Mock<IModuleRegistryService>();
        modules.Setup(x => x.ListModulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ModuleCatalogEntry>
            {
                new(
                    new ModuleDescriptor("hello", "Hello World", "1.0", null, null, null, []),
                    ModuleLifecycleState.Discovered,
                    Installed: false,
                    Enabled: false,
                    EffectivelyActive: false,
                    ValidationIssues: [])
            });

        var sut = Fixture.CreateSut(caps, modules: modules);
        var result = await sut.QueryAsync(new InventoryQuery(), CancellationToken.None);

        result.Items.Single(i => i.Type == InventoryTypeTokens.Module).UpdatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Explicit_module_type_filter_reliably_returns_modules_despite_truncation_elsewhere()
    {
        var caps = Fixture.AllowAll();
        var modules = new Mock<IModuleRegistryService>();
        modules.Setup(x => x.ListModulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ModuleCatalogEntry>
            {
                new(
                    new ModuleDescriptor("hello", "Hello World", "1.0", null, null, null, []),
                    ModuleLifecycleState.Discovered,
                    Installed: false,
                    Enabled: false,
                    EffectivelyActive: false,
                    ValidationIssues: [])
            });

        // Many more-recently-dated integrations than the take budget, so a naive Take() after
        // sorting by "updated desc" would push an undated Module entirely out of the page.
        var integrations = new Mock<IIntegrationManagementService>();
        integrations.Setup(x => x.ListIntegrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(1, 5).Select(i =>
                new DomainIntegration(
                    new IntegrationId($"i{i}"),
                    $"Item {i}",
                    "m",
                    null,
                    IntegrationState.Active,
                    null,
                    Now.AddMinutes(i),
                    Now.AddMinutes(i))).ToList());

        var sut = Fixture.CreateSut(caps, modules: modules, integrations: integrations);

        var filtered = await sut.QueryAsync(
            new InventoryQuery(Type: InventoryTypeTokens.Module, Take: 2),
            CancellationToken.None);
        filtered.Items.Should().ContainSingle(i => i.Type == InventoryTypeTokens.Module);

        var unfiltered = await sut.QueryAsync(new InventoryQuery(Take: 2), CancellationToken.None);
        unfiltered.Items.Should().Contain(i => i.Type == InventoryTypeTokens.Module,
            "default cross-type browsing must not make an entire authorized resource family disappear behind higher-volume types");
    }

    private sealed class Fixture
    {
        public required InventoryQueryService Sut { get; init; }
        public required Mock<IModuleRegistryService> Modules { get; init; }
        public required Mock<IIntegrationManagementService> Integrations { get; init; }
        public required Mock<IConnectionManagementService> Connections { get; init; }
        public required Mock<IAuditTimelineQueryService> Audit { get; init; }

        public static OperatorCapabilitiesDto AllowAll() =>
            new(
                CanManageSchedules: true,
                CanManageRecovery: false,
                CanViewAudit: true,
                CanViewRuntimeHealth: false,
                CanManageModules: true,
                CanViewConnections: true,
                CanViewIntegrations: true,
                CanViewNotificationDefinitions: true);

        public static Fixture AllSources(OperatorCapabilitiesDto? caps = null)
        {
            var modules = new Mock<IModuleRegistryService>();
            modules.Setup(x => x.ListModulesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ModuleCatalogEntry>
                {
                    new(
                        new ModuleDescriptor("hello", "Hello World", "1.0", null, null, null, []),
                        ModuleLifecycleState.InstalledEnabled,
                        Installed: true,
                        Enabled: true,
                        EffectivelyActive: true,
                        [])
                });

            var integrations = new Mock<IIntegrationManagementService>();
            integrations.Setup(x => x.ListIntegrationsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DomainIntegration>
                {
                    new(new IntegrationId("int-1"), "Billing sync", "hello", null, IntegrationState.Active, null, Now, Now)
                });

            var connections = new Mock<IConnectionManagementService>();
            connections.Setup(x => x.ListConnectionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Connection>
                {
                    new(
                        new ConnectionId("conn-1"),
                        new ConnectorDefinitionId("sql"),
                        "Campus DB",
                        new Dictionary<string, string>(),
                        new Dictionary<string, string>(),
                        null,
                        null)
                });

            var schedules = new Mock<IScheduleManagementService>();
            schedules.Setup(x => x.ListDefinitionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ScheduleDefinition>
                {
                    new(
                        new ScheduleId("sched-1"),
                        "Nightly import",
                        "0 0 * * *",
                        "UTC",
                        true,
                        new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
                        null,
                        1)
                });

            var notifications = new Mock<INotificationDefinitionService>();
            notifications.Setup(x => x.ListAsync(It.IsAny<NotificationDefinitionListQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<NotificationDefinition>
                {
                    new(
                        NotificationDefinitionId.From("ndef-1"),
                        "Import completed",
                        null,
                        "platformEvent",
                        "userCreated",
                        "integration",
                        "fullyEditable",
                        null,
                        "int-1",
                        "import.completed",
                        true,
                        null,
                        "{}",
                        "{}",
                        Now,
                        Now,
                        null,
                        null)
                });

            var runs = new Mock<IRunQueryService>();
            runs.Setup(x => x.ListRunsAsync(It.IsAny<RunQueryFilter>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RunSummaryDto>
                {
                    new("run-1", "inst-1", "wf-hello", "completed", Now.AddHours(-2), Now.AddHours(-1), null, null)
                });

            var audit = new Mock<IAuditTimelineQueryService>();
            audit.Setup(x => x.QueryAsync(It.IsAny<AuditFilterCriteria>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AuditTimelinePage(
                [
                    new AuditTimelineEntry(
                        "aud-1",
                        Now.AddHours(-3),
                        "user:a",
                        "integration.update",
                        "integration",
                        "success",
                        "Integration updated",
                        null,
                        [new AuditRelatedReference("run", "run-1")])
                ], null));

            var sut = CreateSut(
                caps ?? AllowAll(),
                modules,
                integrations,
                connections,
                schedules,
                notifications,
                runs,
                audit);

            return new Fixture
            {
                Sut = sut,
                Modules = modules,
                Integrations = integrations,
                Connections = connections,
                Audit = audit
            };
        }

        public static InventoryQueryService CreateSut(
            OperatorCapabilitiesDto caps,
            Mock<IModuleRegistryService>? modules = null,
            Mock<IIntegrationManagementService>? integrations = null,
            Mock<IConnectionManagementService>? connections = null,
            Mock<IScheduleManagementService>? schedules = null,
            Mock<INotificationDefinitionService>? notifications = null,
            Mock<IRunQueryService>? runs = null,
            Mock<IAuditTimelineQueryService>? audit = null)
        {
            var capabilities = new Mock<IOperatorCapabilitiesQueryService>();
            capabilities.Setup(x => x.GetCapabilitiesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(caps);

            if (modules is null)
            {
                modules = new Mock<IModuleRegistryService>();
                modules.Setup(x => x.ListModulesAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Array.Empty<ModuleCatalogEntry>());
            }

            if (integrations is null)
            {
                integrations = new Mock<IIntegrationManagementService>();
                integrations.Setup(x => x.ListIntegrationsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Array.Empty<DomainIntegration>());
            }

            if (connections is null)
            {
                connections = new Mock<IConnectionManagementService>();
                connections.Setup(x => x.ListConnectionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Array.Empty<Connection>());
            }

            if (schedules is null)
            {
                schedules = new Mock<IScheduleManagementService>();
                schedules.Setup(x => x.ListDefinitionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Array.Empty<ScheduleDefinition>());
            }

            if (notifications is null)
            {
                notifications = new Mock<INotificationDefinitionService>();
                notifications.Setup(x => x.ListAsync(It.IsAny<NotificationDefinitionListQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Array.Empty<NotificationDefinition>());
            }

            if (runs is null)
            {
                runs = new Mock<IRunQueryService>();
                runs.Setup(x => x.ListRunsAsync(It.IsAny<RunQueryFilter>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Array.Empty<RunSummaryDto>());
            }

            if (audit is null)
            {
                audit = new Mock<IAuditTimelineQueryService>();
                audit.Setup(x => x.QueryAsync(It.IsAny<AuditFilterCriteria>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new AuditTimelinePage([], null));
            }

            var clock = new FixedTimeProvider(Now);
            return new InventoryQueryService(
                capabilities.Object,
                modules.Object,
                integrations.Object,
                connections.Object,
                schedules.Object,
                notifications.Object,
                runs.Object,
                audit.Object,
                clock);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utc;
        public FixedTimeProvider(DateTimeOffset utc) => _utc = utc;
        public override DateTimeOffset GetUtcNow() => _utc;
    }
}
