using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.Models;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class OperatorCapabilitiesNotificationTests
{
    [Fact]
    public async Task GetCapabilitiesAsync_MapsNotificationDefinitionPermissions()
    {
        var auth = new NotificationAuthStub(
            read: true,
            manage: true);
        var service = new OperatorCapabilitiesQueryService(
            new StubPrincipalContext(),
            auth,
            new AllowScheduleGovernance(),
            new AllowRecoveryGovernance(),
            NullLogger<OperatorCapabilitiesQueryService>.Instance);

        var dto = await service.GetCapabilitiesAsync(CancellationToken.None);

        dto.CanViewNotificationDefinitions.Should().BeTrue();
        dto.CanManageNotificationDefinitions.Should().BeTrue();
    }

    [Fact]
    public async Task GetCapabilitiesAsync_ViewFalse_WhenReadDenied()
    {
        var auth = new NotificationAuthStub(read: false, manage: true);
        var service = new OperatorCapabilitiesQueryService(
            new StubPrincipalContext(),
            auth,
            new AllowScheduleGovernance(),
            new AllowRecoveryGovernance(),
            NullLogger<OperatorCapabilitiesQueryService>.Instance);

        var dto = await service.GetCapabilitiesAsync(CancellationToken.None);

        dto.CanViewNotificationDefinitions.Should().BeFalse();
        dto.CanManageNotificationDefinitions.Should().BeTrue();
    }

    private sealed class NotificationAuthStub : IAuthorizationEvaluationService
    {
        private readonly bool _read;
        private readonly bool _manage;

        public NotificationAuthStub(bool read, bool manage)
        {
            _read = read;
            _manage = manage;
        }

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            if (context.Permission.Resource?.ResourceType != ResourceTypeConstants.NotificationDefinition)
                return ValueTask.FromResult(true);
            if (context.Permission.Action == ActionConstants.NotificationDefinitionRead)
                return ValueTask.FromResult(_read);
            if (context.Permission.Action == ActionConstants.NotificationDefinitionManage)
                return ValueTask.FromResult(_manage);
            return ValueTask.FromResult(false);
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
