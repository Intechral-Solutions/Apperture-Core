using Apperture.ControlPlane.Dashboard;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// A10.2.15 and A10.2.17: the consumers whose authorization changed with the removal of the <c>role.manage</c> implication
/// and the new permissions. Each is asked with an evaluator that allows exactly one permission, so a consumer that still
/// accepts an old permission (a dual-accept) or ignores the new one is caught. The permissions are given as
/// (action, resource type) pairs at instance scope.
/// </summary>
public sealed class AuthorizationSwitchConsumerTests
{
    private static readonly (string Action, string Type) UserReadOnUser = (ActionConstants.UserRead, ResourceTypeConstants.User);
    private static readonly (string Action, string Type) UserReadOnRole = (ActionConstants.UserRead, ResourceTypeConstants.Role);
    private static readonly (string Action, string Type) ScheduleManage = (ActionConstants.ScheduleManage, ResourceTypeConstants.Schedule);
    private static readonly (string Action, string Type) TeamManage = (ActionConstants.OperationalTeamManage, ResourceTypeConstants.OperationalTeam);
    private static readonly (string Action, string Type) DirectorySearch = (ActionConstants.AuthDirectorySearch, ResourceTypeConstants.InstanceConfiguration);
    private static readonly (string Action, string Type) AuthConfigurationRead = (ActionConstants.AuthConfigurationRead, ResourceTypeConstants.InstanceConfiguration);
    private static readonly (string Action, string Type) AuthEntraManage = (ActionConstants.AuthEntraManage, ResourceTypeConstants.InstanceConfiguration);
    private static readonly (string Action, string Type) ConnectionVerify = (ActionConstants.ConnectionVerify, ResourceTypeConstants.Connection);
    private static readonly (string Action, string Type) ConnectionRead = (ActionConstants.ConnectionRead, ResourceTypeConstants.Connection);

    /// <summary>Allows exactly the named permissions at instance scope and remembers everything it was asked.</summary>
    private sealed class OnlyTheseEvaluation(params (string Action, string Type)[] allowed) : IAuthorizationEvaluationService
    {
        public List<(string Action, string Type)> Asked { get; } = [];

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            var asked = (context.Permission.Action, context.Permission.Resource?.ResourceType ?? "");
            Asked.Add(asked);
            return ValueTask.FromResult(allowed.Contains(asked));
        }
    }

    private sealed class SignedInPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new HumanActor(new ActorId("user-1"), "User"));
    }

    private sealed class AllowAllSchedules : IScheduleGovernanceService
    {
        public ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class AllowAllRecovery : IRecoveryGovernanceService
    {
        public ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private static async Task<Apperture.ControlPlane.Security.Models.OperatorCapabilitiesDto> CapabilitiesAsync(params (string Action, string Type)[] allowed)
    {
        var service = new OperatorCapabilitiesQueryService(
            new SignedInPrincipal(),
            new OnlyTheseEvaluation(allowed),
            new AllowAllSchedules(),
            new AllowAllRecovery(),
            NullLogger<OperatorCapabilitiesQueryService>.Instance);
        return await service.GetCapabilitiesAsync(CancellationToken.None);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // P-18 / A10.2.17: the three governance-read consumers check user.read on User
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task CanViewGovernance_follows_user_read_on_User_and_not_user_read_on_Role()
    {
        (await CapabilitiesAsync(UserReadOnUser)).CanViewGovernance.Should().BeTrue();
        (await CapabilitiesAsync(UserReadOnRole)).CanViewGovernance.Should().BeFalse("user.read on Role is obsolete");
    }

    [Fact]
    public async Task A_role_holding_only_user_read_on_User_cannot_manage_users_and_is_not_otherwise_privileged()
    {
        var capabilities = await CapabilitiesAsync(UserReadOnUser);

        capabilities.CanViewGovernance.Should().BeTrue();
        capabilities.CanManageUsers.Should().BeFalse();
        capabilities.CanManageRoles.Should().BeFalse();
        capabilities.CanAssignRoles.Should().BeFalse();
        capabilities.CanAssignPermissions.Should().BeFalse();
    }

    [Fact]
    public async Task The_capability_query_never_asks_for_user_read_on_Role()
    {
        var evaluation = new OnlyTheseEvaluation();
        var service = new OperatorCapabilitiesQueryService(
            new SignedInPrincipal(), evaluation, new AllowAllSchedules(), new AllowAllRecovery(), NullLogger<OperatorCapabilitiesQueryService>.Instance);

        await service.GetCapabilitiesAsync(CancellationToken.None);

        evaluation.Asked.Should().NotContain(UserReadOnRole);
        evaluation.Asked.Should().Contain(UserReadOnUser);
    }

    private static async Task<ActionResult<GovernanceDescriptorResponse>> DescriptorsAsync(params (string Action, string Type)[] allowed)
    {
        var controller = new GovernanceController(
            new InMemoryResourceDescriptorRegistry(),
            new SignedInPrincipal(),
            new OnlyTheseEvaluation(allowed),
            new Mock<IPrincipalToUserResolver>().Object,
            new Mock<IRoleProvider>().Object);
        return await controller.GetDescriptors(CancellationToken.None);
    }

    [Fact]
    public async Task The_descriptors_endpoint_follows_user_read_on_User_and_not_user_read_on_Role()
    {
        (await DescriptorsAsync(UserReadOnUser)).Result.Should().BeOfType<OkObjectResult>();
        (await DescriptorsAsync(UserReadOnRole)).Result.Should().BeOfType<ForbidResult>();
        (await DescriptorsAsync()).Result.Should().BeOfType<ForbidResult>();
    }

    private static async Task<DashboardCard?> GovernanceCardAsync(params (string Action, string Type)[] allowed)
    {
        var roles = new Mock<IRoleProvider>();
        roles.Setup(r => r.GetRolesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return await new GovernanceDashboardCardProvider(roles.Object, new SignedInPrincipal(), new OnlyTheseEvaluation(allowed)).BuildAsync();
    }

    [Fact]
    public async Task The_governance_dashboard_card_follows_user_read_on_User_and_not_user_read_on_Role()
    {
        (await GovernanceCardAsync(UserReadOnUser)).Should().NotBeNull();
        (await GovernanceCardAsync(UserReadOnRole)).Should().BeNull();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // P-17: team management is operational_team.manage, and schedule.manage no longer reaches it
    // ---------------------------------------------------------------------------------------------------------------

    private static OperationalTeamManagementService Teams(OnlyTheseEvaluation evaluation)
    {
        var options = new DbContextOptionsBuilder<AppertureDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        return new OperationalTeamManagementService(new AppertureDbContext(options), new SignedInPrincipal(), evaluation, TimeProvider.System);
    }

    [Fact]
    public async Task Team_management_is_authorized_by_operational_team_manage()
    {
        var teams = Teams(new OnlyTheseEvaluation(TeamManage));

        var list = await teams.ListAsync(includeDisabled: true);
        var created = await teams.CreateAsync(new OperationalTeamCreateCommand("Night shift", null, true));

        list.Should().BeEmpty();
        created.Name.Should().Be("Night shift");
    }

    [Fact]
    public async Task Schedule_manage_alone_no_longer_authorizes_any_team_operation_and_there_is_no_dual_accept()
    {
        var teams = Teams(new OnlyTheseEvaluation(ScheduleManage));

        await ((Func<Task>)(async () => await teams.ListAsync(true))).Should().ThrowAsync<UnauthorizedAccessException>();
        await ((Func<Task>)(async () => await teams.GetAsync("opteam_x"))).Should().ThrowAsync<UnauthorizedAccessException>();
        await ((Func<Task>)(async () => await teams.CreateAsync(new OperationalTeamCreateCommand("x", null, true)))).Should().ThrowAsync<UnauthorizedAccessException>();
        await ((Func<Task>)(async () => await teams.UpdateAsync("opteam_x", new OperationalTeamUpdateCommand("x", null, true)))).Should().ThrowAsync<UnauthorizedAccessException>();
        await ((Func<Task>)(async () => await teams.ReplaceMembersAsync("opteam_x", []))).Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Team_management_asks_for_the_new_permission_and_never_for_schedule_manage()
    {
        var evaluation = new OnlyTheseEvaluation(TeamManage);
        await Teams(evaluation).ListAsync(true);

        evaluation.Asked.Should().Equal(TeamManage);
    }

    [Fact]
    public void The_attention_contact_group_service_stays_on_schedule_manage()
    {
        // D-08 names team management only. The contact-group service is deliberately not switched (P-17).
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src/Apperture.ControlPlane/Apperture.ControlPlane.Security/AttentionContactGroupManagementService.cs"));

        source.Should().Contain("ActionConstants.ScheduleManage");
        source.Should().NotContain("OperationalTeamManage");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // P-19: directory search and test-connect have their own permissions; the flags mirror the routes
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task CanSearchDirectory_is_true_only_for_auth_directory_search()
    {
        (await CapabilitiesAsync(DirectorySearch)).CanSearchDirectory.Should().BeTrue();
        (await CapabilitiesAsync(AuthConfigurationRead)).CanSearchDirectory.Should().BeFalse("the old read permission no longer reaches the search");
        (await CapabilitiesAsync(AuthEntraManage)).CanSearchDirectory.Should().BeFalse("the old manage permission no longer reaches the search");
        (await CapabilitiesAsync()).CanSearchDirectory.Should().BeFalse();
    }

    [Fact]
    public async Task CanVerifyConnections_is_true_only_for_connection_verify()
    {
        (await CapabilitiesAsync(ConnectionVerify)).CanVerifyConnections.Should().BeTrue();
        (await CapabilitiesAsync(ConnectionRead)).CanVerifyConnections.Should().BeFalse("connection.read no longer reaches test-connect");
        (await CapabilitiesAsync()).CanVerifyConnections.Should().BeFalse();
    }

    [Fact]
    public async Task The_new_flags_do_not_move_the_flags_that_already_existed()
    {
        var capabilities = await CapabilitiesAsync(DirectorySearch, ConnectionVerify);

        capabilities.CanViewAuthConfiguration.Should().BeFalse();
        capabilities.CanManageEntraTenants.Should().BeFalse();
        capabilities.CanViewConnections.Should().BeFalse();
        capabilities.CanBrowseConnections.Should().BeFalse();
    }

    [Fact]
    public void The_client_capabilities_record_carries_the_two_flags_as_additive_properties_that_default_to_false()
    {
        var server = typeof(Apperture.ControlPlane.Security.Models.OperatorCapabilitiesDto);
        var client = typeof(Apperture.Management.Client.OperatorCapabilitiesDto);

        foreach (var type in new[] { server, client })
        {
            foreach (var name in new[] { "CanSearchDirectory", "CanVerifyConnections" })
            {
                var parameter = type.GetConstructors().Single().GetParameters().SingleOrDefault(p => p.Name == name);
                parameter.Should().NotBeNull($"{type.FullName} must carry {name}");
                parameter!.HasDefaultValue.Should().BeTrue();
                parameter.DefaultValue.Should().Be(false);
            }
        }

        // Additive: new flags come last, so an existing positional construction keeps its meaning.
        var parameters = server.GetConstructors().Single().GetParameters().Select(p => p.Name).ToList();
        parameters.TakeLast(2).Should().Equal("CanSearchDirectory", "CanVerifyConnections");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Source guards: the routes check the new permissions and never the old ones
    // ---------------------------------------------------------------------------------------------------------------

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Source(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot(), "src", relative));

    [Fact]
    public void The_two_directory_search_routes_are_guarded_by_the_search_permission_and_not_by_the_read_or_manage_check()
    {
        var source = Source("Apperture.ControlPlane/Apperture.Management.Api/Controllers/ConfigEntraTenantsController.cs");
        var users = MethodBody(source, "SearchDirectoryUsers(");
        var groups = MethodBody(source, "SearchDirectoryGroups(");

        foreach (var body in new[] { users, groups })
        {
            body.Should().Contain("AuthorizeDirectorySearchAsync");
            body.Should().NotContain("AuthorizeReadAsync");
            body.Should().NotContain("AuthorizeManageAsync");
        }

        source.Should().Contain("ActionConstants.AuthDirectorySearch");
    }

    [Fact]
    public void Test_connect_is_guarded_by_connection_verify_and_not_by_connection_read()
    {
        var source = Source("Apperture.ControlPlane/Apperture.ControlPlane.Security/ConnectionManagementService.cs");
        var body = MethodBody(source, "VerifyConnectionAsync(");

        body.Should().Contain("ActionConstants.ConnectionVerify");
        body.Should().NotContain("ActionConstants.ConnectionRead");
    }

    /// <summary>The text from the declaration containing <paramref name="signature"/> to the next member at the same indentation.</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"{signature} must exist");
        var next = source.IndexOf("\n    [Http", start, StringComparison.Ordinal);
        var nextMember = source.IndexOf("\n    public async", start + signature.Length, StringComparison.Ordinal);
        var nextPrivate = source.IndexOf("\n    private", start + signature.Length, StringComparison.Ordinal);
        var end = new[] { next, nextMember, nextPrivate }.Where(i => i > start).DefaultIfEmpty(source.Length).Min();
        return source[start..end];
    }
}
