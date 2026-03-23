using Apperture.ControlPlane.Dashboard;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Recovery;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class DashboardFoundationsTests
{
    [Fact]
    public async Task Composer_OrdersAndFiltersNullCards()
    {
        var providers = new IDashboardCardProvider[]
        {
            new StubProvider(30, new DashboardCard("c3", "summary", "Card 3", 30, null, DashboardCardAttention.None, null, [])),
            new StubProvider(10, null),
            new StubProvider(20, new DashboardCard("c2", "summary", "Card 2", 20, null, DashboardCardAttention.None, null, []))
        };

        var sut = new DashboardDefaultViewService(providers, NullLogger<DashboardDefaultViewService>.Instance);
        var view = await sut.GetDefaultAsync();

        view.Scope.Should().Be(DashboardDefaultViewService.SharedPlatformOperatorScope);
        view.ProviderFailureCount.Should().Be(0);
        view.Cards.Select(c => c.Id).Should().ContainInOrder("c2", "c3");
    }

    [Fact]
    public async Task Composer_ProviderThrows_IsolatesFailure_AddsPartialLoadPlaceholder()
    {
        var providers = new IDashboardCardProvider[]
        {
            new ThrowingProvider(5),
            new StubProvider(20, new DashboardCard("ok", "summary", "OK", 20, null, DashboardCardAttention.None, "fine", []))
        };

        var sut = new DashboardDefaultViewService(providers, NullLogger<DashboardDefaultViewService>.Instance);
        var view = await sut.GetDefaultAsync();

        view.ProviderFailureCount.Should().Be(1);
        view.Cards.Should().Contain(c => c.Id == "ok");
        view.Cards.Should().Contain(c => c.Id == "dashboard-partial-load");
        view.Cards.Last().Id.Should().Be("dashboard-partial-load");
    }

    [Fact]
    public async Task BlockedWorkProvider_MapsCountsAndAttention()
    {
        var recovery = new Mock<IRecoveryQueryService>();
        recovery.Setup(x => x.GetBlockedWorkRemediationAsync("local", 24, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new BlockedWorkRemediationEntry("run-1", "wf-1", "workflow", "mod-1", DateTimeOffset.UtcNow, "failed", true, "ok"),
                new BlockedWorkRemediationEntry("run-2", "wf-2", "workflow", "mod-1", DateTimeOffset.UtcNow, "failed", false, "blocked")
            ]);

        var governance = new Mock<IRecoveryGovernanceService>();
        governance.Setup(x => x.CanReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var sut = new BlockedWorkDashboardCardProvider(recovery.Object, governance.Object);

        var card = await sut.BuildAsync();

        card.Should().NotBeNull();
        card!.Attention.Should().Be(DashboardCardAttention.Warning);
        card.Facts.Should().Contain(f => f.Label == "Blocked runs" && f.Value == "2");
        card.Facts.Should().Contain(f => f.Label == "Retry-eligible now" && f.Value == "1");
    }

    [Fact]
    public async Task DashboardController_MapsServiceResponse()
    {
        var service = new Mock<IDashboardDefaultViewService>();
        service.Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardDefaultView(
                DashboardDefaultViewService.SharedPlatformOperatorScope,
                DashboardDefaultViewService.DefaultTitle,
                [new DashboardCard("runtime-health", "summary", "Runtime Health", 10, "/control/center", DashboardCardAttention.Warning, "Degraded", [new DashboardCardFact("Anomalies", "3")])],
                ProviderFailureCount: 0));

        var controller = new DashboardController(service.Object);
        var action = await controller.GetDefault();

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var json = ok.Value.Should().BeAssignableTo<Apperture.Management.Api.Models.DashboardDefaultResponse>().Subject;
        json.Scope.Should().Be(DashboardDefaultViewService.SharedPlatformOperatorScope);
        json.ProviderFailureCount.Should().Be(0);
        json.Cards.Should().HaveCount(1);
        json.Cards[0].Attention.Should().Be("Warning");
        json.Cards[0].Facts.Should().Contain(f => f.Label == "Anomalies" && f.Value == "3");
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

        public ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_card);
    }

    private sealed class ThrowingProvider : IDashboardCardProvider
    {
        public ThrowingProvider(int order) => Order = order;

        public int Order { get; }

        public ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated provider failure");
    }
}
