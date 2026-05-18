using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class OperatorCapabilitiesIntegrationNotificationTests
{
    [Fact]
    public async Task IntegrationNotificationView_RequiresBothIntegrationReadAndNotificationRead()
    {
        var auth = new IntegrationNotificationAuthStub(integrationRead: true, notificationRead: false);
        var service = CreateService(auth);
        var dto = await service.GetCapabilitiesAsync(CancellationToken.None);
        dto.CanViewIntegrations.Should().BeTrue();
        dto.CanViewNotificationDefinitions.Should().BeFalse();
    }

    [Fact]
    public async Task IntegrationNotificationManage_RequiresNotificationManageAndIntegrationUpdate()
    {
        var auth = new IntegrationNotificationAuthStub(
            integrationRead: true,
            notificationRead: true,
            integrationUpdate: true,
            notificationManage: true);
        var service = CreateService(auth);
        var dto = await service.GetCapabilitiesAsync(CancellationToken.None);

        dto.CanUpdateIntegrations.Should().BeTrue();
        dto.CanManageNotificationDefinitions.Should().BeTrue();
        CanManageIntegrationNotifications(dto).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrationNotificationManage_WithCreateOnly_DoesNotGrantUpdateCapability()
    {
        var auth = new IntegrationNotificationAuthStub(
            integrationRead: true,
            notificationRead: true,
            integrationCreate: true,
            integrationUpdate: false,
            notificationManage: true);
        var service = CreateService(auth);
        var dto = await service.GetCapabilitiesAsync(CancellationToken.None);

        dto.CanManageIntegrations.Should().BeTrue();
        dto.CanUpdateIntegrations.Should().BeFalse();
        dto.CanManageNotificationDefinitions.Should().BeTrue();
        CanManageIntegrationNotifications(dto).Should().BeFalse(
            "integration notification UI manage gates on integration.update, not integration.create");
    }

    [Fact]
    public async Task CutoverReadinessView_DoesNotRequireIntegrationUpdate()
    {
        var auth = new IntegrationNotificationAuthStub(
            integrationRead: true,
            notificationRead: true,
            integrationUpdate: false,
            notificationManage: false);
        var service = CreateService(auth);
        var dto = await service.GetCapabilitiesAsync(CancellationToken.None);

        (dto.CanViewIntegrations && dto.CanViewNotificationDefinitions).Should().BeTrue();
        dto.CanUpdateIntegrations.Should().BeFalse();
    }

    [Fact]
    public async Task IntegrationNotificationManage_WithUpdateOnly_DoesNotGrantCreateCapability()
    {
        var auth = new IntegrationNotificationAuthStub(
            integrationUpdate: true,
            integrationCreate: false);
        var service = CreateService(auth);
        var dto = await service.GetCapabilitiesAsync(CancellationToken.None);

        dto.CanUpdateIntegrations.Should().BeTrue();
        dto.CanManageIntegrations.Should().BeFalse();
    }

    private static bool CanManageIntegrationNotifications(Apperture.ControlPlane.Security.Models.OperatorCapabilitiesDto caps) =>
        caps.CanUpdateIntegrations && caps.CanManageNotificationDefinitions;

    private static OperatorCapabilitiesQueryService CreateService(IAuthorizationEvaluationService auth) =>
        new(
            new StubPrincipalContext(),
            auth,
            new AllowScheduleGovernance(),
            new AllowRecoveryGovernance(),
            NullLogger<OperatorCapabilitiesQueryService>.Instance);

    private sealed class IntegrationNotificationAuthStub : IAuthorizationEvaluationService
    {
        private readonly bool _integrationRead;
        private readonly bool _notificationRead;
        private readonly bool _integrationCreate;
        private readonly bool _integrationUpdate;
        private readonly bool _notificationManage;

        public IntegrationNotificationAuthStub(
            bool integrationRead = false,
            bool notificationRead = false,
            bool integrationCreate = false,
            bool integrationUpdate = false,
            bool notificationManage = false)
        {
            _integrationRead = integrationRead;
            _notificationRead = notificationRead;
            _integrationCreate = integrationCreate;
            _integrationUpdate = integrationUpdate;
            _notificationManage = notificationManage;
        }

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            var perm = context.Permission;
            if (perm.Action == ActionConstants.IntegrationRead && perm.Resource?.ResourceType == ResourceTypeConstants.Integration)
                return ValueTask.FromResult(_integrationRead);
            if (perm.Action == ActionConstants.NotificationDefinitionRead && perm.Resource?.ResourceType == ResourceTypeConstants.NotificationDefinition)
                return ValueTask.FromResult(_notificationRead);
            if (perm.Action == ActionConstants.IntegrationCreate && perm.Resource?.ResourceType == ResourceTypeConstants.Integration)
                return ValueTask.FromResult(_integrationCreate);
            if (perm.Action == ActionConstants.IntegrationUpdate && perm.Resource?.ResourceType == ResourceTypeConstants.Integration)
                return ValueTask.FromResult(_integrationUpdate);
            if (perm.Action == ActionConstants.NotificationDefinitionManage && perm.Resource?.ResourceType == ResourceTypeConstants.NotificationDefinition)
                return ValueTask.FromResult(_notificationManage);
            return ValueTask.FromResult(true);
        }
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new StubActor());
    }

    private sealed class StubActor : IActor
    {
        public ActorId Id => new("user:test");
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName => "Test";
    }

    private sealed class AllowScheduleGovernance : IScheduleGovernanceService
    {
        public ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class AllowRecoveryGovernance : IRecoveryGovernanceService
    {
        public ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }
}
