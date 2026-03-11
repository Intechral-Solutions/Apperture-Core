using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.ControlPlane.Security;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class RbacAuthorizationPolicyEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_denies_when_actor_is_null()
    {
        var evaluator = new RbacAuthorizationPolicyEvaluator(
            new StubPrincipalToUserResolver(),
            new StubRoleProvider(),
            new ScopeContainmentEvaluator());

        var result = await evaluator.EvaluateAsync(
            null!,
            new PermissionDescriptor(ActionConstants.ConnectionRead),
            null,
            null);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_denies_when_stub_resolver_returns_null()
    {
        var actor = new TestActor(new ActorId("user-1"), ActorKind.Human, "Test User");
        var evaluator = new RbacAuthorizationPolicyEvaluator(
            new StubPrincipalToUserResolver(),
            new StubRoleProvider(),
            new ScopeContainmentEvaluator());

        var result = await evaluator.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.ConnectionRead),
            null,
            null);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_allows_when_user_has_matching_role_permission()
    {
        var userId = new UserId("user-1");
        var actor = new TestActor(new ActorId("user-1"), ActorKind.Human, "Test User");
        var role = new Role(
            new RoleId("role-1"),
            "Admin",
            null,
            [new PermissionDescriptor(ActionConstants.ConnectionRead, new ResourceDescriptor(ResourceTypeConstants.Connection, ScopeKeyConstants.Instance))],
            true);

        var userResolver = new Mock<IPrincipalToUserResolver>();
        userResolver.Setup(r => r.ResolveUserIdAsync(actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);

        var roleProvider = new Mock<IRoleProvider>();
        roleProvider.Setup(r => r.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([role]);

        var evaluator = new RbacAuthorizationPolicyEvaluator(
            userResolver.Object,
            roleProvider.Object,
            new ScopeContainmentEvaluator());

        var result = await evaluator.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.ConnectionRead, new ResourceDescriptor(ResourceTypeConstants.Connection, ScopeKeyConstants.Instance)),
            null,
            null);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DelegatingAuthorizationService_forwards_to_policy_evaluator()
    {
        var policyEvaluator = new Mock<IAuthorizationPolicyEvaluator>();
        policyEvaluator.Setup(e => e.EvaluateAsync(It.IsAny<IActor>(), It.IsAny<PermissionDescriptor>(), It.IsAny<ResourceDescriptor?>(), It.IsAny<IGovernedResourceIdentity?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new DelegatingAuthorizationService(policyEvaluator.Object);
        var actor = new TestActor(new ActorId("a1"), ActorKind.Human, "A");
        var permission = new PermissionDescriptor(ActionConstants.AuditRead);

        var result = await service.EvaluateAsync(actor, permission, null);

        result.Should().BeTrue();
        policyEvaluator.Verify(e => e.EvaluateAsync(actor, permission, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class TestActor : IActor
    {
        public ActorId Id { get; }
        public ActorKind Kind { get; }
        public string? DisplayName { get; }

        public TestActor(ActorId id, ActorKind kind, string? displayName)
        {
            Id = id;
            Kind = kind;
            DisplayName = displayName;
        }
    }
}
