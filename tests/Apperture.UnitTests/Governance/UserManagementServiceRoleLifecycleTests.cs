using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class UserManagementServiceRoleLifecycleTests
{
    [Fact]
    public async Task CreateRole_without_role_manage_is_rejected_and_writes_no_lifecycle_audit()
    {
        var harness = Harness.Create(allowRoleManage: false);

        var act = () => harness.Service.CreateRoleAsync("Operators").AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        harness.Roles.Created.Should().BeEmpty();
        harness.Audit.Actions.Should().NotContain(GovernanceAuditActionNames.RoleCreated);
    }

    [Fact]
    public async Task CreateRole_generates_opaque_id_and_never_sets_system_role()
    {
        var harness = Harness.Create();

        var role = await harness.Service.CreateRoleAsync("  Operators  ", "  ", []);

        role.Id.Value.Should().StartWith("role-");
        role.Id.Value.Should().HaveLength("role-".Length + 32);
        role.Id.Value.Should().MatchRegex("^role-[0-9a-f]{32}$");
        role.Id.Value.Should().NotBe(BootstrapConstants.PlatformAdminRoleId);
        role.Id.Value.Should().NotBe("Operators");
        role.Name.Should().Be("Operators");
        role.Description.Should().BeNull();
        role.IsSystemRole.Should().BeFalse();
        role.Permissions.Should().BeEmpty();
        harness.Roles.Created.Should().ContainSingle(r => r.Id.Value == role.Id.Value && r.IsSystemRole == false);
        harness.Audit.Actions.Should().Contain(GovernanceAuditActionNames.RoleCreated);
    }

    [Fact]
    public async Task CreateRole_with_empty_grants_does_not_require_permission_assign()
    {
        var harness = Harness.Create(allowPermissionAssign: false);

        var role = await harness.Service.CreateRoleAsync("Readers");

        role.Permissions.Should().BeEmpty();
        harness.Roles.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateRole_with_initial_grants_requires_permission_assign()
    {
        var harness = Harness.Create(allowPermissionAssign: false);
        var grant = KnownPermission();

        var act = () => harness.Service.CreateRoleAsync("Readers", null, [grant]).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        harness.Roles.Created.Should().BeEmpty();
        harness.Audit.Actions.Should().NotContain(GovernanceAuditActionNames.RoleCreated);
    }

    [Fact]
    public async Task CreateRole_rejects_unknown_permission_without_writing()
    {
        var harness = Harness.Create();
        var unknown = new PermissionDescriptor("unknown.action", new ResourceDescriptor("UnknownType", "instance"));

        var act = () => harness.Service.CreateRoleAsync("Readers", null, [unknown]).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not in the canonical governance catalog*");
        harness.Roles.Created.Should().BeEmpty();
        harness.Audit.Actions.Should().NotContain(GovernanceAuditActionNames.RoleCreated);
    }

    [Fact]
    public async Task CreateRole_rejects_duplicate_trimmed_name()
    {
        var harness = Harness.Create();
        await harness.Service.CreateRoleAsync("Operators");

        var act = () => harness.Service.CreateRoleAsync("  operators  ").AsTask();

        var ex = await act.Should().ThrowAsync<RoleLifecycleException>();
        ex.Which.Code.Should().Be(RoleLifecycleErrorCodes.DuplicateName);
        harness.Roles.Created.Should().HaveCount(1);
        harness.Audit.Actions.Should().NotContain(a => a == GovernanceAuditActionNames.RoleCreated && harness.Roles.Created.Count > 1);
    }

    [Fact]
    public async Task UpdateRoleMetadata_refuses_system_role_and_audits_rejection()
    {
        var harness = Harness.Create();
        var systemId = new RoleId(BootstrapConstants.PlatformAdminRoleId);

        var act = () => harness.Service.UpdateRoleMetadataAsync(systemId, "Renamed", "no").AsTask();

        var ex = await act.Should().ThrowAsync<RoleLifecycleException>();
        ex.Which.Code.Should().Be(RoleLifecycleErrorCodes.SystemRole);
        harness.Roles.MetadataUpdates.Should().BeEmpty();
        harness.Audit.Actions.Should().Contain(GovernanceAuditActionNames.RoleMutationRejected);
        harness.Audit.LastDetails.Should().Contain("reason=system_role");
        harness.Audit.Actions.Should().NotContain(GovernanceAuditActionNames.RoleMetadataUpdated);
    }

    [Fact]
    public async Task UpdateRoleMetadata_missing_role_writes_no_audit()
    {
        var harness = Harness.Create();

        var act = () => harness.Service.UpdateRoleMetadataAsync(new RoleId("role-" + new string('a', 32)), "Name", null).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        harness.Audit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateRoleMetadata_updates_name_and_nulls_empty_description()
    {
        var harness = Harness.Create();
        var created = await harness.Service.CreateRoleAsync("Operators", "Team");
        harness.Audit.Actions.Clear();

        var updated = await harness.Service.UpdateRoleMetadataAsync(created.Id, "  Reviewers  ", "   ");

        updated.Name.Should().Be("Reviewers");
        updated.Description.Should().BeNull();
        updated.IsSystemRole.Should().BeFalse();
        harness.Roles.MetadataUpdates.Should().ContainSingle(u => u.RoleId == created.Id.Value && u.Name == "Reviewers" && u.Description == null);
        harness.Audit.Actions.Should().Contain(GovernanceAuditActionNames.RoleMetadataUpdated);
    }

    [Fact]
    public async Task UpdateRoleMetadata_rejects_duplicate_name_and_leaves_the_row()
    {
        var harness = Harness.Create();
        await harness.Service.CreateRoleAsync("Operators");
        var second = await harness.Service.CreateRoleAsync("Reviewers", "Keep");
        harness.Audit.Actions.Clear();

        var act = () => harness.Service.UpdateRoleMetadataAsync(second.Id, "  operators  ", "Changed").AsTask();

        var ex = await act.Should().ThrowAsync<RoleLifecycleException>();
        ex.Which.Code.Should().Be(RoleLifecycleErrorCodes.DuplicateName);
        var stored = await harness.Roles.GetAsync(second.Id);
        stored!.Name.Should().Be("Reviewers");
        stored.Description.Should().Be("Keep");
        harness.Roles.MetadataUpdates.Should().BeEmpty();
        harness.Audit.Actions.Should().NotContain(GovernanceAuditActionNames.RoleMetadataUpdated);
        harness.Audit.Actions.Should().NotContain(GovernanceAuditActionNames.RoleMutationRejected);
    }

    [Fact]
    public async Task SetRolePermissions_refuses_system_role_without_writing_grants()
    {
        var harness = Harness.Create();
        var systemId = new RoleId(BootstrapConstants.PlatformAdminRoleId);

        var act = () => harness.Service.SetRolePermissionsAsync(systemId, [KnownPermission()]).AsTask();

        var ex = await act.Should().ThrowAsync<RoleLifecycleException>();
        ex.Which.Code.Should().Be(RoleLifecycleErrorCodes.SystemRole);
        harness.Roles.LastUpdatedPermissions.Should().BeNull();
        harness.Audit.Actions.Should().Contain(GovernanceAuditActionNames.RoleMutationRejected);
        harness.Audit.Actions.Should().NotContain(GovernanceAuditActionNames.PermissionAssignmentUpdated);
    }

    [Fact]
    public async Task DeleteRole_refuses_system_role()
    {
        var harness = Harness.Create();
        var systemId = new RoleId(BootstrapConstants.PlatformAdminRoleId);

        var act = () => harness.Service.DeleteRoleAsync(systemId).AsTask();

        var ex = await act.Should().ThrowAsync<RoleLifecycleException>();
        ex.Which.Code.Should().Be(RoleLifecycleErrorCodes.SystemRole);
        harness.Roles.Deleted.Should().BeEmpty();
        harness.Audit.Actions.Should().Contain(GovernanceAuditActionNames.RoleMutationRejected);
        harness.Audit.Actions.Should().NotContain(GovernanceAuditActionNames.RoleDeleted);
    }

    [Fact]
    public async Task DeleteRole_with_assignments_refuses_and_does_not_unassign()
    {
        var harness = Harness.Create();
        var created = await harness.Service.CreateRoleAsync("Operators");
        await harness.Roles.AssignToUserAsync(new UserId("user-1"), created.Id);
        harness.Audit.Actions.Clear();

        var act = () => harness.Service.DeleteRoleAsync(created.Id).AsTask();

        var ex = await act.Should().ThrowAsync<RoleLifecycleException>();
        ex.Which.Code.Should().Be(RoleLifecycleErrorCodes.AssignmentsRemain);
        ex.Which.AssignmentCount.Should().Be(1);
        harness.Roles.Created.Should().ContainSingle(r => r.Id.Value == created.Id.Value);
        harness.Roles.Assigned.Should().ContainSingle(a => a.RoleId == created.Id.Value);
        harness.Roles.RemovedFromUser.Should().BeEmpty();
        harness.Audit.Actions.Should().Contain(GovernanceAuditActionNames.RoleMutationRejected);
        harness.Audit.LastDetails.Should().Contain("reason=assignments_remain");
        harness.Audit.Actions.Should().NotContain(GovernanceAuditActionNames.RoleDeleted);
    }

    [Fact]
    public async Task DeleteRole_with_zero_assignments_removes_the_role_row()
    {
        var harness = Harness.Create();
        var created = await harness.Service.CreateRoleAsync("Operators");
        harness.Audit.Actions.Clear();

        await harness.Service.DeleteRoleAsync(created.Id);

        harness.Roles.Created.Should().BeEmpty();
        harness.Roles.Deleted.Should().ContainSingle(id => id == created.Id.Value);
        harness.Roles.RemovedFromUser.Should().BeEmpty();
        (await harness.Roles.GetAsync(created.Id)).Should().BeNull();
        harness.Audit.Actions.Should().Contain(GovernanceAuditActionNames.RoleDeleted);
    }

    [Fact]
    public async Task DeleteRole_missing_writes_no_audit()
    {
        var harness = Harness.Create();

        var act = () => harness.Service.DeleteRoleAsync(new RoleId("role-" + new string('b', 32))).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        harness.Audit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRoleAssignments_returns_holders_and_count()
    {
        var harness = Harness.Create();
        var created = await harness.Service.CreateRoleAsync("Operators");
        var userId = new UserId("user-holders");
        harness.Users.Seed(new User(userId, "Pat Lee", [], [], IsBreakGlassEligible: false));
        await harness.Roles.AssignToUserAsync(userId, created.Id);

        var list = await harness.Service.GetRoleAssignmentsAsync(created.Id);

        list.Should().NotBeNull();
        list!.Count.Should().Be(1);
        list.Holders.Should().ContainSingle(h => h.UserId.Value == userId.Value && h.DisplayName == "Pat Lee");
    }

    [Fact]
    public async Task GetRoleAssignments_without_user_read_is_rejected()
    {
        var harness = Harness.Create(allowUserRead: false);

        var act = () => harness.Service.GetRoleAssignmentsAsync(new RoleId(BootstrapConstants.PlatformAdminRoleId)).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private static PermissionDescriptor KnownPermission() =>
        new(ActionConstants.UserRead, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance));

    private sealed class Harness
    {
        private Harness(IUserManagementService service, RecordingRoles roles, RecordingUsers users, RecordingAudit audit)
        {
            Service = service;
            Roles = roles;
            Users = users;
            Audit = audit;
        }

        public IUserManagementService Service { get; }
        public RecordingRoles Roles { get; }
        public RecordingUsers Users { get; }
        public RecordingAudit Audit { get; }

        public static Harness Create(
            bool allowRoleManage = true,
            bool allowPermissionAssign = true,
            bool allowUserRead = true)
        {
            var registry = new InMemoryResourceDescriptorRegistry();
            registry.RegisterPermission(KnownPermission(), ResourceFamily.Core);

            var roles = new RecordingRoles();
            var users = new RecordingUsers();
            var audit = new RecordingAudit();
            var service = new UserManagementService(
                new StubPrincipal(),
                new StubResolver(),
                new ConfigurableAuth(allowRoleManage, allowPermissionAssign, allowUserRead),
                users,
                roles,
                registry,
                new StubCredentials(),
                new StubHasher(),
                audit,
                Options.Create(new LocalAuthOptions()));
            return new Harness(service, roles, users, audit);
        }
    }

    private sealed class ConfigurableAuth : IAuthorizationService
    {
        private readonly bool _roleManage;
        private readonly bool _permissionAssign;
        private readonly bool _userRead;

        public ConfigurableAuth(bool roleManage, bool permissionAssign, bool userRead)
        {
            _roleManage = roleManage;
            _permissionAssign = permissionAssign;
            _userRead = userRead;
        }

        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default)
        {
            var allowed = permission.Action switch
            {
                ActionConstants.RoleManage => _roleManage,
                ActionConstants.PermissionAssign => _permissionAssign,
                ActionConstants.UserRead => _userRead,
                _ => false
            };
            return ValueTask.FromResult(allowed);
        }
    }

    private sealed class RecordingRoles : IRoleRepository
    {
        private readonly Dictionary<string, Role> _roles = new(StringComparer.Ordinal);

        public RecordingRoles()
        {
            var adminId = new RoleId(BootstrapConstants.PlatformAdminRoleId);
            _roles[adminId.Value] = new Role(adminId, "Platform Administrator", null, [], IsSystemRole: true);
        }

        public List<Role> Created { get; } = [];
        public List<(string RoleId, string Name, string? Description)> MetadataUpdates { get; } = [];
        public List<string> Deleted { get; } = [];
        public List<(string UserId, string RoleId)> Assigned { get; } = [];
        public List<(string UserId, string RoleId)> RemovedFromUser { get; } = [];
        public IReadOnlyList<PermissionDescriptor>? LastUpdatedPermissions { get; private set; }

        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_roles.TryGetValue(roleId.Value, out var role) ? role : null);

        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>(_roles.Values.ToList());

        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Assigned.Count(a => a.RoleId == roleId.Value));

        public ValueTask<IReadOnlyList<UserId>> ListUserIdsWithRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<UserId>>(Assigned.Where(a => a.RoleId == roleId.Value).Select(a => new UserId(a.UserId)).ToList());

        public ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default)
        {
            Assigned.Add((userId.Value, roleId.Value));
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default)
        {
            RemovedFromUser.Add((userId.Value, roleId.Value));
            Assigned.RemoveAll(a => a.UserId == userId.Value && a.RoleId == roleId.Value);
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default)
        {
            LastUpdatedPermissions = permissions;
            return ValueTask.CompletedTask;
        }

        public ValueTask CreateAsync(Role role, CancellationToken cancellationToken = default)
        {
            if (_roles.Values.Any(r => string.Equals(r.Name, role.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A role with this name already exists.");
            _roles[role.Id.Value] = role;
            Created.Add(role);
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateMetadataAsync(RoleId roleId, string name, string? description, CancellationToken cancellationToken = default)
        {
            if (!_roles.TryGetValue(roleId.Value, out var existing))
                throw new InvalidOperationException($"Role {roleId.Value} not found.");
            if (_roles.Values.Any(r => r.Id.Value != roleId.Value && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A role with this name already exists.");
            var updated = existing with { Name = name, Description = description };
            _roles[roleId.Value] = updated;
            MetadataUpdates.Add((roleId.Value, name, description));
            return ValueTask.CompletedTask;
        }

        public ValueTask<RoleUnassignedDeleteResult> TryDeleteUnassignedAsync(RoleId roleId, CancellationToken cancellationToken = default)
        {
            if (!_roles.TryGetValue(roleId.Value, out var existing))
                return ValueTask.FromResult(new RoleUnassignedDeleteResult(RoleUnassignedDeleteOutcome.NotFound));

            var count = Assigned.Count(a => a.RoleId == roleId.Value);
            if (count > 0)
                return ValueTask.FromResult(new RoleUnassignedDeleteResult(RoleUnassignedDeleteOutcome.AssignmentsRemain, count, existing.Name));

            _roles.Remove(roleId.Value);
            Created.RemoveAll(r => r.Id.Value == roleId.Value);
            Deleted.Add(roleId.Value);
            return ValueTask.FromResult(new RoleUnassignedDeleteResult(RoleUnassignedDeleteOutcome.Deleted, 0, existing.Name));
        }
    }

    private sealed class RecordingUsers : IUserRepository
    {
        private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);

        public void Seed(User user) => _users[user.Id.Value] = user;

        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Count);

        public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.TryGetValue(id.Value, out var user) ? user : null);

        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<User?>(null);

        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default)
        {
            _users[user.Id.Value] = user;
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default)
        {
            _users[user.Id.Value] = user;
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<(IReadOnlyList<User>, int)>((_users.Values.Skip(skip).Take(take).ToList(), _users.Count));

        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingAudit : IAuditService
    {
        public List<string> Actions { get; } = [];
        public string? LastDetails { get; private set; }

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Actions.Add(record.Action);
            LastDetails = record.Details;
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage([], null));

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AuditRecord?>(null);
    }

    private sealed class StubCredentials : ILocalCredentialRepository
    {
        public ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<LocalCredentialData?>(null);

        public ValueTask SetDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class StubHasher : ILocalPasswordHasher
    {
        public string AlgorithmName => "stub";
        public (string Hash, string Salt) HashPassword(string password) => ("h", "s");
        public bool VerifyPassword(string password, string hash, string salt) => false;
    }

    private sealed class StubPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new HumanActor(new ActorId("admin"), "admin"));
    }

    private sealed class StubResolver : IPrincipalToUserResolver
    {
        public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<UserId?>(new UserId("admin"));
    }
}
