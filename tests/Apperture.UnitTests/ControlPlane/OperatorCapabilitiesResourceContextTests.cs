using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// CTX-3A: OperatorCapabilitiesDto.CanManageResourceContexts (resource_context.manage on ResourceContext).
/// </summary>
public sealed class OperatorCapabilitiesResourceContextTests
{
    [Fact]
    public async Task GetCapabilitiesAsync_WhenGrantedResourceContextManage_SetsCanManageResourceContexts()
    {
        var auth = new ResourceContextAuthStub(resourceContextManage: true);
        var service = CreateService(auth);

        var caps = await service.GetCapabilitiesAsync(CancellationToken.None);

        caps.CanManageResourceContexts.Should().BeTrue();
    }

    [Fact]
    public async Task GetCapabilitiesAsync_WithoutGrant_DeniesCanManageResourceContexts()
    {
        var auth = new ResourceContextAuthStub(resourceContextManage: false);
        var service = CreateService(auth);

        var caps = await service.GetCapabilitiesAsync(CancellationToken.None);

        caps.CanManageResourceContexts.Should().BeFalse();
    }

    private static OperatorCapabilitiesQueryService CreateService(IAuthorizationEvaluationService auth) =>
        new(
            new StubPrincipalContext(),
            auth,
            new AllowScheduleGovernance(),
            new AllowRecoveryGovernance(),
            NullLogger<OperatorCapabilitiesQueryService>.Instance);

    private sealed class ResourceContextAuthStub : IAuthorizationEvaluationService
    {
        private readonly bool _resourceContextManage;

        public ResourceContextAuthStub(bool resourceContextManage = false)
        {
            _resourceContextManage = resourceContextManage;
        }

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            var perm = context.Permission;
            if (perm.Action == ActionConstants.ResourceContextManage && perm.Resource?.ResourceType == ResourceTypeConstants.ResourceContext)
                return ValueTask.FromResult(_resourceContextManage);
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
