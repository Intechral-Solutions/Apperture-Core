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

public sealed class UserManagementServiceCreateUserTests
{
    [Fact]
    public async Task CreateUser_without_user_manage_is_rejected()
    {
        var harness = Harness.Create(allowUserManage: false);

        var act = () => harness.Service.CreateUserAsync("Operator", IdentitySource.Local, "operator", "Password1!").AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        harness.Users.Added.Should().BeEmpty();
        harness.Credentials.Hashes.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateUser_creates_local_user_link_and_hashed_credential()
    {
        var harness = Harness.Create();

        var userId = await harness.Service.CreateUserAsync("Operator", IdentitySource.Local, "  Operator  ", "Password1!");

        var user = await harness.Users.GetByIdAsync(userId);
        user.Should().NotBeNull();
        user!.DisplayName.Should().Be("Operator");
        user.IdentityLinks.Should().ContainSingle(l =>
            l.Source == IdentitySource.Local && l.ExternalId == "operator");
        user.AssignedRoleIds.Should().BeEmpty();
        harness.Hasher.LastPassword.Should().Be("Password1!");
        harness.Credentials.Hashes[userId.Value].Should().Be("h");
        harness.Credentials.Disabled[userId.Value].Should().BeFalse();
        harness.Audit.Actions.Should().Contain("user.created");
    }

    [Fact]
    public async Task CreateUser_rejects_duplicate_local_username()
    {
        var harness = Harness.Create();
        await harness.Service.CreateUserAsync("First", IdentitySource.Local, "operator", "Password1!");

        var act = () => harness.Service.CreateUserAsync("Second", IdentitySource.Local, "OPERATOR", "Password1!").AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
        harness.Users.Added.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateUser_requires_a_password_for_local_users()
    {
        var harness = Harness.Create();

        var act = () => harness.Service.CreateUserAsync("Operator", IdentitySource.Local, "operator").AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*password*");
        harness.Users.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateUser_rejects_a_password_shorter_than_the_minimum()
    {
        var harness = Harness.Create();

        var act = () => harness.Service.CreateUserAsync("Operator", IdentitySource.Local, "operator", "short").AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*at least*");
        harness.Users.Added.Should().BeEmpty();
        harness.Credentials.Hashes.Should().BeEmpty();
    }

    [Fact]
    public async Task AssignRole_without_role_assign_is_rejected()
    {
        var harness = Harness.Create(allowRoleAssign: false);
        var userId = await harness.Service.CreateUserAsync("Operator", IdentitySource.Local, "operator", "Password1!");

        var act = () => harness.Service.AssignRoleAsync(userId, new RoleId(BootstrapConstants.PlatformAdminRoleId)).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        harness.Roles.Assigned.Should().BeEmpty();
    }

    [Fact]
    public async Task AssignRole_assigns_platform_administrator_when_authorized()
    {
        var harness = Harness.Create();
        var userId = await harness.Service.CreateUserAsync("Second admin", IdentitySource.Local, "admin2", "Password1!");
        var roleId = new RoleId(BootstrapConstants.PlatformAdminRoleId);

        await harness.Service.AssignRoleAsync(userId, roleId);

        harness.Roles.Assigned.Should().ContainSingle(a => a.UserId == userId.Value && a.RoleId == roleId.Value);
        harness.Audit.Actions.Should().Contain(GovernanceAuditActionNames.RoleAssigned);
    }

    private sealed class Harness
    {
        public RecordingUsers Users { get; } = new();
        public RecordingCredentials Credentials { get; } = new();
        public RecordingRoles Roles { get; } = new();
        public RecordingAudit Audit { get; } = new();
        public RecordingHasher Hasher { get; } = new();
        public UserManagementService Service { get; }

        private Harness(bool allowUserManage, bool allowRoleAssign)
        {
            Service = new UserManagementService(
                new StubPrincipal(),
                new StubResolver(),
                new SelectiveAuth(allowUserManage, allowRoleAssign),
                Users,
                Roles,
                new InMemoryResourceDescriptorRegistry(),
                Credentials,
                Hasher,
                Audit,
                Options.Create(new LocalAuthOptions { MinPasswordLength = 8 }));
        }

        public static Harness Create(bool allowUserManage = true, bool allowRoleAssign = true) =>
            new(allowUserManage, allowRoleAssign);
    }

    private sealed class SelectiveAuth : IAuthorizationService
    {
        private readonly bool _userManage;
        private readonly bool _roleAssign;

        public SelectiveAuth(bool userManage, bool roleAssign)
        {
            _userManage = userManage;
            _roleAssign = roleAssign;
        }

        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default)
        {
            if (permission.Action == ActionConstants.UserManage)
                return ValueTask.FromResult(_userManage);
            if (permission.Action == ActionConstants.RoleAssign)
                return ValueTask.FromResult(_roleAssign);
            return ValueTask.FromResult(true);
        }
    }

    private sealed class RecordingUsers : IUserRepository
    {
        private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);
        public List<User> Added { get; } = [];

        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Count);

        public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.GetValueOrDefault(id.Value));

        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u =>
                u.IdentityLinks.Any(l => l.Source == source && l.ExternalId == externalId)));

        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default)
        {
            _users[user.Id.Value] = user;
            Added.Add(user);
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
            ValueTask.FromResult<(IReadOnlyList<User>, int)>((_users.Values.ToList(), _users.Count));

        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingCredentials : ILocalCredentialRepository
    {
        public Dictionary<string, string> Hashes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, bool> Disabled { get; } = new(StringComparer.Ordinal);

        public ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) =>
            Hashes.TryGetValue(userId.Value, out var hash)
                ? ValueTask.FromResult<LocalCredentialData?>(new LocalCredentialData(userId, hash, "s", "stub", Disabled[userId.Value]))
                : ValueTask.FromResult<LocalCredentialData?>(null);

        public ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default)
        {
            Hashes[userId.Value] = passwordHash;
            Disabled[userId.Value] = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask SetDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default)
        {
            Disabled[userId.Value] = disabled;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingRoles : IRoleRepository
    {
        public List<(string UserId, string RoleId)> Assigned { get; } = [];

        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Role?>(new Role(roleId, "Role", null, [], IsSystemRole: true));

        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Assigned.Count(a => a.RoleId == roleId.Value));

        public ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default)
        {
            Assigned.Add((userId.Value, roleId.Value));
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingAudit : IAuditService
    {
        public List<string> Actions { get; } = [];

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Actions.Add(record.Action);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage([], null));

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AuditRecord?>(null);
    }

    private sealed class RecordingHasher : ILocalPasswordHasher
    {
        public string AlgorithmName => "stub";
        public string? LastPassword { get; private set; }
        public (string Hash, string Salt) HashPassword(string password)
        {
            LastPassword = password;
            return ("h", "s");
        }

        public bool VerifyPassword(string password, string hash, string salt) => true;
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
