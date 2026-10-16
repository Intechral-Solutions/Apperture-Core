using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// <c>connection.browse</c>: the single new Services action. Seeded on Connection at instance scope in the Connector family,
/// held by the platform administrator role (and, like every permission, by a break-glass session), additive to
/// <c>connection.read</c>, and never implied by it.
/// </summary>
public sealed class ConnectionBrowsePermissionTests
{
    private sealed class StubEnvironmentContext : IEnvironmentContext
    {
        public string EnvironmentId => "local";
    }

    [Fact]
    public void The_action_is_seeded_on_connection_at_instance_scope_in_the_connector_family()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new StubEnvironmentContext()).Seed(registry);

        var browse = registry.GetPermissionDescriptors().Where(p => p.Action == "connection.browse").ToList();

        browse.Should().ContainSingle();
        browse[0].Resource!.ResourceType.Should().Be(ResourceTypeConstants.Connection);
        browse[0].Resource!.ScopeKey.Should().Be(ScopeKeyConstants.Instance);
        ActionConstants.ConnectionBrowse.Should().Be("connection.browse");
    }

    [Fact]
    public void It_is_the_only_new_action_and_no_service_or_generic_tool_or_sql_execute_action_exists()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new StubEnvironmentContext()).Seed(registry);

        var actions = registry.GetPermissionDescriptors().Select(p => p.Action).ToList();

        actions.Should().NotContain(a => a.StartsWith("service.", StringComparison.OrdinalIgnoreCase));
        actions.Should().NotContain(a => a.Contains("sql", StringComparison.OrdinalIgnoreCase) && a.Contains("execute", StringComparison.OrdinalIgnoreCase));
        actions.Should().NotContain(a => a.StartsWith("tool.", StringComparison.OrdinalIgnoreCase) || a.StartsWith("connection.tool", StringComparison.OrdinalIgnoreCase));
        actions.Where(a => a.StartsWith("connection.", StringComparison.Ordinal)).Should().BeEquivalentTo(
            ["connection.create", "connection.read", "connection.update", "connection.delete", "connection.browse", "connection.verify"]);
    }

    [Fact]
    public void The_platform_administrator_role_receives_it_through_the_repair_set()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new StubEnvironmentContext()).Seed(registry);

        var repaired = PlatformAdminRolePermissionRepair.ApplyAllRepairs([], registry);

        repaired.Should().Contain(p => p.Action == ActionConstants.ConnectionBrowse && p.Resource!.ScopeKey == ScopeKeyConstants.Instance);
    }

    [Fact]
    public void Reading_connections_does_not_bring_browse_with_it_in_the_repair_set()
    {
        // The repair set that grants connection.read is a different set; a role that only ever received that set has no browse.
        var readOnlySet = PlatformAdminRolePermissionRepair.RequiredConnectorOperatorPermissions;

        readOnlySet.Should().Contain(p => p.Action == ActionConstants.ConnectionRead);
        readOnlySet.Should().NotContain(p => p.Action == ActionConstants.ConnectionBrowse);
        PlatformAdminRolePermissionRepair.RequiredConnectionBrowsePermissions.Should().ContainSingle()
            .Which.Action.Should().Be(ActionConstants.ConnectionBrowse);
    }

    // Capability flag --------------------------------------------------------------------------------

    private static async Task<bool> CanBrowseAsync(bool read, bool browse)
    {
        var service = new OperatorCapabilitiesQueryService(
            new StubPrincipalContext(),
            new GrantingAuth(read, browse),
            new AllowScheduleGovernance(),
            new AllowRecoveryGovernance(),
            NullLogger<OperatorCapabilitiesQueryService>.Instance);
        return (await service.GetCapabilitiesAsync(CancellationToken.None)).CanBrowseConnections;
    }

    [Fact]
    public async Task The_capability_is_true_only_for_a_caller_holding_both_read_and_browse()
    {
        (await CanBrowseAsync(read: true, browse: true)).Should().BeTrue();
        (await CanBrowseAsync(read: true, browse: false)).Should().BeFalse("reading connections is not browsing them");
        (await CanBrowseAsync(read: false, browse: true)).Should().BeFalse("browse without read grants nothing");
        (await CanBrowseAsync(read: false, browse: false)).Should().BeFalse();
    }

    private sealed class GrantingAuth(bool read, bool browse) : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(context.Permission.Action switch
            {
                ActionConstants.ConnectionRead => read,
                ActionConstants.ConnectionBrowse => browse,
                _ => false
            });
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

    // Against the REAL evaluator ---------------------------------------------------------------------

    private static async Task<bool> EvaluateBrowseAsync(params PermissionDescriptor[] rolePermissions)
    {
        var userId = new Apperture.Core.Domain.Identity.UserId("user-1");
        var actor = new TestHumanActor();
        var role = new Apperture.Core.Domain.Identity.Role(
            new Apperture.Core.Domain.Identity.RoleId("role-1"), "Connection operator", null, rolePermissions, true);

        var resolver = new Moq.Mock<IPrincipalToUserResolver>();
        resolver.Setup(r => r.ResolveUserIdAsync(actor, It.IsAny<CancellationToken>())).ReturnsAsync(userId);
        var roles = new Moq.Mock<IRoleProvider>();
        roles.Setup(r => r.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync([role]);

        var evaluator = new RbacAuthorizationPolicyEvaluator(resolver.Object, roles.Object, new ScopeContainmentEvaluator());
        return await evaluator.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.ConnectionBrowse, new ResourceDescriptor(ResourceTypeConstants.Connection, ScopeKeyConstants.Instance)),
            null,
            null);
    }

    private static PermissionDescriptor OnConnection(string action) =>
        new(action, new ResourceDescriptor(ResourceTypeConstants.Connection, ScopeKeyConstants.Instance));

    [Fact]
    public async Task Every_other_connection_grant_together_still_does_not_authorize_browse()
    {
        (await EvaluateBrowseAsync(
            OnConnection(ActionConstants.ConnectionRead),
            OnConnection(ActionConstants.ConnectionCreate),
            OnConnection(ActionConstants.ConnectionUpdate),
            OnConnection(ActionConstants.ConnectionDelete),
            new PermissionDescriptor(ActionConstants.RoleManage, new ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance))))
            .Should().BeFalse("browse is an explicit grant; no other connection or role-management grant implies it");
    }

    [Fact]
    public async Task An_explicit_browse_grant_authorizes_browse()
    {
        (await EvaluateBrowseAsync(OnConnection(ActionConstants.ConnectionBrowse))).Should().BeTrue();
    }

    [Fact]
    public async Task A_browse_grant_on_a_different_resource_type_authorizes_nothing_on_connections()
    {
        (await EvaluateBrowseAsync(new PermissionDescriptor(
            ActionConstants.ConnectionBrowse, new ResourceDescriptor(ResourceTypeConstants.Integration, ScopeKeyConstants.Instance))))
            .Should().BeFalse();
    }

    private sealed class TestHumanActor : IActor
    {
        public ActorId Id => new("user-1");
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName => "Test";
    }
}
