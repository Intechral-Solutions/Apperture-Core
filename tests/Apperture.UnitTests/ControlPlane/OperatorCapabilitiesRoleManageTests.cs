using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class OperatorCapabilitiesRoleManageTests
{
    [Fact]
    public async Task GetCapabilitiesAsync_WhenGrantedRoleManage_SetsCanManageRoles()
    {
        var service = CreateService(allowRoleManage: true);

        var caps = await service.GetCapabilitiesAsync(CancellationToken.None);

        caps.CanManageRoles.Should().BeTrue();
    }

    [Fact]
    public async Task GetCapabilitiesAsync_WithoutRoleManage_DeniesCanManageRoles()
    {
        var service = CreateService(allowRoleManage: false);

        var caps = await service.GetCapabilitiesAsync(CancellationToken.None);

        caps.CanManageRoles.Should().BeFalse();
    }

    private static OperatorCapabilitiesQueryService CreateService(bool allowRoleManage) =>
        new(
            new StubPrincipalContext(),
            new RoleManageAuthStub(allowRoleManage),
            new AllowScheduleGovernance(),
            new AllowRecoveryGovernance(),
            NullLogger<OperatorCapabilitiesQueryService>.Instance);

    private sealed class RoleManageAuthStub : IAuthorizationEvaluationService
    {
        private readonly bool _allowRoleManage;

        public RoleManageAuthStub(bool allowRoleManage) => _allowRoleManage = allowRoleManage;

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            var perm = context.Permission;
            var granted = _allowRoleManage
                && perm.Action == ActionConstants.RoleManage
                && perm.Resource?.ResourceType == ResourceTypeConstants.Role;
            return ValueTask.FromResult(granted);
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
