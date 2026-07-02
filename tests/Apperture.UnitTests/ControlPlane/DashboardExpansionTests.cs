using Apperture.ControlPlane.Dashboard;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ControlFacade;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Domain.Scheduling;
using Apperture.Core.Domain.Workflow;
using Apperture.Management.Api.Controllers;
using Apperture.Shared.Kernel;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class DashboardExpansionTests
{
    [Fact]
    public void AttentionConvention_NoViableSignIn_IsCritical()
    {
        DashboardCardAttentionConvention.FromAuthPosture(noViableSignIn: true, warningCount: 0)
            .Should().Be(DashboardCardAttention.Critical);
    }

    [Fact]
    public void AttentionConvention_FailedRuns_UsesWarningNotCritical()
    {
        DashboardCardAttentionConvention.FromVisibleFailedRunsCount(3)
            .Should().Be(DashboardCardAttention.Warning);
    }

    [Fact]
    public void AttentionConvention_MaxSeverityToken_PicksWorstAcrossList()
    {
        DashboardCardAttentionConvention.MaxAttentionSeverityToken(["low", "high", "medium"])
            .Should().Be("high");
        DashboardCardAttentionConvention.MaxAttentionSeverityToken(["low", "medium"])
            .Should().Be("medium");
        DashboardCardAttentionConvention.MaxAttentionSeverityToken([])
            .Should().Be("none");
    }

    [Fact]
    public async Task ExecutionsProvider_NoActor_OmitsCard()
    {
        var runs = new Mock<IRunQueryService>();
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IActor?)null);

        var sut = new ExecutionsDashboardCardProvider(runs.Object, principal.Object);
        var card = await sut.BuildAsync();

        card.Should().BeNull();
        runs.Verify(x => x.ListRunsAsync(It.IsAny<RunQueryFilter?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecutionsProvider_BuildsCountsAndDrillInWhenFailed()
    {
        var from = DateTimeOffset.UtcNow.AddHours(-24);
        var all =
            new List<RunSummaryDto>
            {
                new("a", "wi", "def1", WorkflowLifecycleStatusNames.Failed, from.AddHours(1), null, null, null),
                new("b", "wi", "def1", WorkflowLifecycleStatusNames.Running, from.AddHours(2), null, null, null),
                new("c", "wi", "def1", WorkflowLifecycleStatusNames.Completed, from.AddHours(3), from.AddHours(4), null, null)
            };
        var runs = new Mock<IRunQueryService>();
        runs.Setup(x => x.ListRunsAsync(It.Is<RunQueryFilter?>(f => f != null && f.Status == WorkflowLifecycleStatusNames.Failed), It.IsAny<CancellationToken>()))
            .ReturnsAsync(all.Where(r => r.Status == WorkflowLifecycleStatusNames.Failed).ToList());
        runs.Setup(x => x.ListRunsAsync(It.Is<RunQueryFilter?>(f => f != null && string.IsNullOrWhiteSpace(f.Status)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(all);

        var principal = new Mock<IPrincipalContext>();
        principal.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IActor>());

        var sut = new ExecutionsDashboardCardProvider(runs.Object, principal.Object);
        var card = await sut.BuildAsync();

        card.Should().NotBeNull();
        card!.Attention.Should().Be(DashboardCardAttention.Warning);
        card.NavigationHref.Should().Contain("status=failed");
        card.NavigationHref.Should().Contain("createdFrom=");
        card.NavigationLabel.Should().Be("View failed runs");
        card.Facts.Should().Contain(f => f.Label == "Failed" && f.Value == "1");
    }

    [Fact]
    public async Task ExecutionsProvider_NoFailed_DrillInUsesWindowWithoutStatus()
    {
        var from = DateTimeOffset.UtcNow.AddHours(-24);
        var all = new List<RunSummaryDto>
        {
            new("b", "wi", "def1", WorkflowLifecycleStatusNames.Running, from.AddHours(2), null, null, null)
        };
        var runs = new Mock<IRunQueryService>();
        runs.Setup(x => x.ListRunsAsync(It.Is<RunQueryFilter?>(f => f != null && f.Status == WorkflowLifecycleStatusNames.Failed), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        runs.Setup(x => x.ListRunsAsync(It.Is<RunQueryFilter?>(f => f != null && string.IsNullOrWhiteSpace(f.Status)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(all);

        var principal = new Mock<IPrincipalContext>();
        principal.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IActor>());

        var sut = new ExecutionsDashboardCardProvider(runs.Object, principal.Object);
        var card = await sut.BuildAsync();

        card.Should().NotBeNull();
        card!.NavigationHref.Should().Contain("createdFrom=");
        card.NavigationHref.Should().NotContain("status=");
        card.NavigationLabel.Should().Be("View runs");
    }

    [Fact]
    public async Task SchedulesProvider_CannotManage_OmitsCard()
    {
        var gov = new Mock<IScheduleGovernanceService>();
        gov.Setup(x => x.CanManageAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var mgmt = new Mock<IScheduleManagementService>();
        var health = new Mock<IRuntimeHealthQueryService>();
        var principal = new Mock<IPrincipalContext>();
        var authz = new Mock<IAuthorizationEvaluationService>();

        var sut = new SchedulesDashboardCardProvider(gov.Object, mgmt.Object, health.Object, principal.Object, authz.Object);
        var card = await sut.BuildAsync();

        card.Should().BeNull();
        mgmt.Verify(x => x.ListDefinitionsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SchedulesProvider_BuildsEnabledDisabledFacts()
    {
        var gov = new Mock<IScheduleGovernanceService>();
        gov.Setup(x => x.CanManageAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var mgmt = new Mock<IScheduleManagementService>();
        mgmt.Setup(x => x.ListDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ScheduleDefinition(
                    new ScheduleId("s1"),
                    "A",
                    "0 * * * *",
                    "UTC",
                    true,
                    new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
                    null,
                    1),
                new ScheduleDefinition(
                    new ScheduleId("s2"),
                    "B",
                    "0 * * * *",
                    "UTC",
                    false,
                    new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
                    null,
                    1)
            ]);

        var health = new Mock<IRuntimeHealthQueryService>();
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IActor>());
        var authz = new Mock<IAuthorizationEvaluationService>();
        authz.Setup(x => x.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = new SchedulesDashboardCardProvider(gov.Object, mgmt.Object, health.Object, principal.Object, authz.Object);
        var card = await sut.BuildAsync();

        card.Should().NotBeNull();
        card!.NavigationHref.Should().Be("/schedules");
        card.Facts.Should().Contain(f => f.Label == "Definitions" && f.Value == "2");
        card.Facts.Should().Contain(f => f.Label == "Enabled" && f.Value == "1");
        card.Facts.Should().Contain(f => f.Label == "Disabled" && f.Value == "1");
        health.Verify(x => x.GetSnapshotAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SchedulesProvider_WithRuntimeHealth_AddsDegradedFact()
    {
        var gov = new Mock<IScheduleGovernanceService>();
        gov.Setup(x => x.CanManageAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var mgmt = new Mock<IScheduleManagementService>();
        mgmt.Setup(x => x.ListDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ScheduleDefinition(
                    new ScheduleId("s1"),
                    "A",
                    "0 * * * *",
                    "UTC",
                    true,
                    new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
                    null,
                    1)
            ]);

        var snapshot = new RuntimeHealthSnapshot(
            DateTimeOffset.UtcNow,
            RuntimeHealthStatus.Healthy,
            [],
            [],
            [
                new ScheduleDiagnosticEntry(
                    "s1",
                    "A",
                    true,
                    0,
                    "{}",
                    null,
                    null,
                    null,
                    null,
                    null,
                    ScheduleHealthStatus.Degraded,
                    "fail",
                    true)
            ],
            [],
            [],
            [],
            []);

        var health = new Mock<IRuntimeHealthQueryService>();
        health.Setup(x => x.GetSnapshotAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var principal = new Mock<IPrincipalContext>();
        principal.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IActor>());
        var authz = new Mock<IAuthorizationEvaluationService>();
        authz.Setup(x => x.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new SchedulesDashboardCardProvider(gov.Object, mgmt.Object, health.Object, principal.Object, authz.Object);
        var card = await sut.BuildAsync();

        card.Should().NotBeNull();
        card!.Attention.Should().Be(DashboardCardAttention.Warning);
        card.Facts.Should().Contain(f => f.Label == "Degraded or blocked (runtime)" && f.Value == "1");
    }

    [Fact]
    public async Task AuditProvider_NoPermission_OmitsCard()
    {
        var audit = new Mock<IAuditService>();
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IActor>());
        var authz = new Mock<IAuthorizationEvaluationService>();
        authz.Setup(x => x.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = new AuditDashboardCardProvider(audit.Object, principal.Object, authz.Object);
        var card = await sut.BuildAsync();

        card.Should().BeNull();
        audit.Verify(x => x.CountAsync(It.IsAny<AuditQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AuditProvider_UsesCountAndDrillIn()
    {
        var audit = new Mock<IAuditService>();
        audit.Setup(x => x.CountAsync(It.IsAny<AuditQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(12);

        var principal = new Mock<IPrincipalContext>();
        principal.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IActor>());
        var authz = new Mock<IAuthorizationEvaluationService>();
        authz.Setup(x => x.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new AuditDashboardCardProvider(audit.Object, principal.Object, authz.Object);
        var card = await sut.BuildAsync();

        card.Should().NotBeNull();
        card!.NavigationHref.Should().Be("/control/audit?hours=24");
        card.Attention.Should().Be(DashboardCardAttention.Info);
        card.Facts.Should().Contain(f => f.Label == "Events in window" && f.Value == "12");
    }

    [Fact]
    public async Task GovernanceProvider_NoPermission_OmitsCard()
    {
        var roles = new Mock<IRoleProvider>();
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IActor>());
        var authz = new Mock<IAuthorizationEvaluationService>();
        authz.Setup(x => x.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = new GovernanceDashboardCardProvider(roles.Object, principal.Object, authz.Object);
        var card = await sut.BuildAsync();

        card.Should().BeNull();
        roles.Verify(x => x.GetRolesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GovernanceProvider_CountsCustomRoles()
    {
        var roles = new Mock<IRoleProvider>();
        roles.Setup(x => x.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Role(new RoleId("r1"), "Sys", null, [], IsSystemRole: true),
                new Role(new RoleId("r2"), "Custom", null, [], IsSystemRole: false)
            ]);

        var principal = new Mock<IPrincipalContext>();
        principal.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IActor>());
        var authz = new Mock<IAuthorizationEvaluationService>();
        authz.Setup(x => x.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new GovernanceDashboardCardProvider(roles.Object, principal.Object, authz.Object);
        var card = await sut.BuildAsync();

        card.Should().NotBeNull();
        card!.Facts.Should().Contain(f => f.Label == "Roles" && f.Value == "2");
        card.Facts.Should().Contain(f => f.Label == "Custom roles" && f.Value == "1");
        card.NavigationHref.Should().Be("/config/governance/roles");
        card.NavigationLabel.Should().Be("View roles");
    }

    [Fact]
    public async Task AttentionProvider_UsesMaxSeverity_NotFirstItemOnly()
    {
        var attention = new Mock<IControlAttentionQueryService>();
        attention.Setup(x => x.ListAttentionAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ControlAttentionItemDto("k", "low", "t", "id", "a", DateTimeOffset.UtcNow.ToString("o"), null),
                new ControlAttentionItemDto("k", "high", "t", "id2", "b", DateTimeOffset.UtcNow.ToString("o"), null)
            ]);

        var sut = new AttentionDashboardCardProvider(attention.Object);
        var card = await sut.BuildAsync();

        card.Should().NotBeNull();
        card!.Attention.Should().Be(DashboardCardAttention.Critical);
        card.Facts.Should().Contain(f => f.Label == "Highest severity" && f.Value == "High");
    }

    [Fact]
    public async Task RuntimeHealthProvider_BlockedWork_DrillInToBlockedWorkPage()
    {
        var snapshot = new RuntimeHealthSnapshot(
            DateTimeOffset.UtcNow,
            RuntimeHealthStatus.Degraded,
            ["r"],
            [],
            [],
            [new BlockedDeferredWorkEntry("run1", "d", "workflow", "m", DateTimeOffset.UtcNow, "x")],
            [],
            [],
            []);

        var health = new Mock<IRuntimeHealthQueryService>();
        health.Setup(x => x.GetSnapshotAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var principal = new Mock<IPrincipalContext>();
        principal.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IActor>());
        var authz = new Mock<IAuthorizationEvaluationService>();
        authz.Setup(x => x.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new RuntimeHealthDashboardCardProvider(health.Object, principal.Object, authz.Object);
        var card = await sut.BuildAsync();

        card.Should().NotBeNull();
        card!.NavigationHref.Should().Be("/control/blocked-work");
        card.NavigationLabel.Should().Be("Open blocked work");
    }

    [Fact]
    public void IntegrationFleetAttentionConvention_Blocking_IsCritical()
    {
        DashboardCardAttentionConvention.FromIntegrationFleetSignals(1, 0, 0, 0)
            .Should().Be(DashboardCardAttention.Critical);
        DashboardCardAttentionConvention.FromIntegrationFleetSignals(0, 2, 0, 1)
            .Should().Be(DashboardCardAttention.Warning);
    }

    [Fact]
    public async Task IntegrationsProvider_BuildsFleetSummaryAndDrillIn()
    {
        var now = DateTimeOffset.UtcNow;
        var readiness = new Apperture.Core.Application.Integration.IntegrationReadinessProjection(
            "int-1",
            "mod-1",
            false,
            1,
            0,
            []);
        var attention = Apperture.Core.Application.Integration.IntegrationFleetAttention.Compute("Active", readiness);
        var row = new Apperture.Core.Application.Integration.IntegrationFleetRowProjection(
            "int-1",
            "Test",
            "mod-1",
            null,
            "Active",
            now,
            now,
            readiness,
            attention,
            new Apperture.Core.Application.Integration.IntegrationFleetOperationalRunAttention(
                0, null, null, null, null, null, false, false, false, now),
            new Apperture.Core.Application.Integration.IntegrationFleetExpectedRunAttention(
                false, false, null, null, null, null, "none", null, 0, true, false, null, null, null),
            ["blocking"],
            [],
            false);

        var readinessSvc = new Mock<IIntegrationReadinessService>();
        readinessSvc.Setup(x => x.ListFleetAsync(It.IsAny<Apperture.Core.Application.Integration.IntegrationFleetQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Apperture.Core.Application.Integration.IntegrationFleetRowProjection> { row });

        var principal = new Mock<IPrincipalContext>();
        principal.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IActor>());
        var authz = new Mock<IAuthorizationEvaluationService>();
        authz.Setup(x => x.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new IntegrationsDashboardCardProvider(readinessSvc.Object, principal.Object, authz.Object);
        var card = await sut.BuildAsync();

        card.Should().NotBeNull();
        card!.Attention.Should().Be(DashboardCardAttention.Critical);
        card.NavigationHref.Should().Be("/integrations/int-1");
        card.NavigationLabel.Should().Be("Open integration");
    }

    [Fact]
    public async Task Composer_IncludesNewCardsInOrder()
    {
        var providers = new IDashboardCardProvider[]
        {
            new StubProvider(38, new DashboardCard("auth", "summary", "Auth", 38, null, DashboardCardAttention.None, null, [])),
            new StubProvider(10, new DashboardCard("health", "summary", "Health", 10, null, DashboardCardAttention.None, null, [])),
            new StubProvider(12, new DashboardCard("blocked", "summary", "Blocked", 12, null, DashboardCardAttention.None, null, [])),
            new StubProvider(25, new DashboardCard("ex", "summary", "Ex", 25, null, DashboardCardAttention.None, null, [])),
            new StubProvider(20, new DashboardCard("attn", "summary", "Attn", 20, null, DashboardCardAttention.None, null, [])),
            new StubProvider(30, new DashboardCard("act", "summary", "Act", 30, null, DashboardCardAttention.None, null, [])),
            new StubProvider(32, new DashboardCard("sch", "summary", "Sch", 32, null, DashboardCardAttention.None, null, []))
        };

        var sut = new DashboardDefaultViewService(providers, NullLogger<DashboardDefaultViewService>.Instance);
        var view = await sut.GetDefaultAsync();

        view.Cards.Select(c => c.Id).Should().ContainInOrder("health", "blocked", "attn", "ex", "act", "sch", "auth");
    }

    [Fact]
    public async Task DashboardController_MapsNavigationLabel()
    {
        var service = new Mock<IDashboardDefaultViewService>();
        service.Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardDefaultView(
                DashboardDefaultViewService.SharedPlatformOperatorScope,
                DashboardDefaultViewService.DefaultTitle,
                [new DashboardCard("x", "summary", "T", 1, "/runs", DashboardCardAttention.None, "ok", [], NavigationLabel: "View runs")],
                ProviderFailureCount: 0));

        var controller = new DashboardController(service.Object);
        var action = await controller.GetDefault();

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var json = ok.Value.Should().BeAssignableTo<Apperture.Management.Api.Models.DashboardDefaultResponse>().Subject;
        json.Cards[0].NavigationLabel.Should().Be("View runs");
    }

    private sealed class StubProvider : IDashboardCardProvider
    {
        private readonly DashboardCard? _card;
        public StubProvider(int order, DashboardCard? card)
        {
            Order = order;
            _card = card;
        }

        public int Order { get; }

        public ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_card);
    }
}
