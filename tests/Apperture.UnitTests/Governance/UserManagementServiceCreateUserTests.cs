using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.UnitTests.Audit;
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

        var act = () => harness.Service.CreateUserAsync("Operator", IdentitySource.Local, "operator", "unique-local-passphrase").AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        harness.Users.Added.Should().BeEmpty();
        harness.Credentials.Hashes.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateUser_creates_local_user_link_and_hashed_credential()
    {
        var harness = Harness.Create();

        var userId = await harness.Service.CreateUserAsync("Operator", IdentitySource.Local, "  Operator  ", "unique-local-passphrase");

        var user = await harness.Users.GetByIdAsync(userId);
        user.Should().NotBeNull();
        user!.DisplayName.Should().Be("Operator");
        user.IdentityLinks.Should().ContainSingle(l =>
            l.Source == IdentitySource.Local && l.ExternalId == "operator");
        user.Username.Should().Be("operator", "the normalized sign-in name is the username");
        user.AssignedRoleIds.Should().BeEmpty();
        harness.Hasher.LastPassword.Should().Be("unique-local-passphrase");
        harness.Credentials.Hashes[userId.Value].Should().Be("h");
        harness.Credentials.Disabled[userId.Value].Should().BeFalse();

        // AUD-1.2: the user, its credential and the row commit in one unit; the password is never recorded.
        harness.Audit.Actions.Should().Equal("user.created");
        var row = harness.Audit.Last;
        row.Target!.Id.Should().Be(userId.Value);
        row.Fields["username"].Should().Be("operator");
        row.Fields["localSignInReady"].Should().Be(true);
        row.Fields["setupInvitationRequested"].Should().Be(false);
        row.Fields.Values.Should().NotContain("unique-local-passphrase").And.NotContain("h");
        harness.Audit.Events.Single().Actor.Should().Be(AuditActor.CurrentPrincipal);
    }

    [Fact]
    public async Task CreateUser_that_fails_after_the_user_was_added_commits_neither_user_nor_row()
    {
        var harness = Harness.Create();
        harness.Credentials.FailNextSet = true;

        var act = () => harness.Service.CreateUserAsync("Operator", IdentitySource.Local, "operator", "unique-local-passphrase").AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.Audit.Actions.Should().BeEmpty("the operation did not complete, so its unit commits nothing");
    }

    [Fact]
    public async Task CreateUser_rejects_duplicate_local_username()
    {
        var harness = Harness.Create();
        await harness.Service.CreateUserAsync("First", IdentitySource.Local, "operator", "unique-local-passphrase");

        var act = () => harness.Service.CreateUserAsync("Second", IdentitySource.Local, "OPERATOR", "unique-local-passphrase").AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
        harness.Users.Added.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("jane@contoso.com", UsernameErrorCodes.Invalid)]
    [InlineData("op", UsernameErrorCodes.Invalid)]
    [InlineData("op-", UsernameErrorCodes.Invalid)]
    [InlineData("system", UsernameErrorCodes.Reserved)]
    [InlineData("worker", UsernameErrorCodes.Reserved)]
    public async Task CreateUser_requires_a_new_username_to_satisfy_the_policy(string username, string code)
    {
        var harness = Harness.Create();

        var act = () => harness.Service.CreateUserAsync("Operator", IdentitySource.Local, username, "unique-local-passphrase").AsTask();

        (await act.Should().ThrowAsync<UsernameException>()).Which.Code.Should().Be(code);
        harness.Users.Added.Should().BeEmpty();
        harness.Credentials.Hashes.Should().BeEmpty();
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
    public async Task CreateUser_rejects_a_common_password()
    {
        var harness = Harness.Create();

        var act = () => harness.Service.CreateUserAsync("Operator", IdentitySource.Local, "operator", "Password1!").AsTask();

        await act.Should().ThrowAsync<PasswordPolicyException>();
        harness.Users.Added.Should().BeEmpty();
        harness.Credentials.Hashes.Should().BeEmpty();
    }

    [Fact]
    public async Task AssignRole_without_role_assign_is_rejected()
    {
        var harness = Harness.Create(allowRoleAssign: false);
        var userId = await harness.Service.CreateUserAsync("Operator", IdentitySource.Local, "operator", "unique-local-passphrase");

        var act = () => harness.Service.AssignRoleAsync(userId, new RoleId(BootstrapConstants.PlatformAdminRoleId)).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        harness.Roles.Assigned.Should().BeEmpty();
    }

    [Fact]
    public async Task AssignRole_assigns_platform_administrator_when_authorized()
    {
        var harness = Harness.Create();
        var userId = await harness.Service.CreateUserAsync("Second admin", IdentitySource.Local, "admin2", "unique-local-passphrase");
        var roleId = new RoleId(BootstrapConstants.PlatformAdminRoleId);

        await harness.Service.AssignRoleAsync(userId, roleId);

        harness.Roles.Assigned.Should().ContainSingle(a => a.UserId == userId.Value && a.RoleId == roleId.Value);
        var row = harness.Audit.Single(GovernanceAuditActionNames.RoleAssigned);
        row.Target!.Id.Should().Be(userId.Value, "the affected user is the target; the administrator is the actor");
        row.Fields["roleId"].Should().Be(roleId.Value);
    }

    [Fact]
    public async Task AssignRole_already_held_changes_nothing_and_records_nothing()
    {
        var harness = Harness.Create();
        var roleId = new RoleId(BootstrapConstants.PlatformAdminRoleId);
        harness.Users.Seed(new User(new UserId("held-1"), "Held", [], [roleId], IsBreakGlassEligible: false) { Username = "held1" });

        await harness.Service.AssignRoleAsync(new UserId("held-1"), roleId);

        harness.Roles.Assigned.Should().BeEmpty();
        harness.Audit.Actions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("breakglass")]
    [InlineData("BreakGlass")]
    [InlineData("  BREAKGLASS  ")]
    public async Task CreateUser_rejects_reserved_emergency_username(string username)
    {
        var harness = Harness.Create();

        var act = () => harness.Service.CreateUserAsync("Operator", IdentitySource.Local, username, "unique-local-passphrase").AsTask();

        var ex = await act.Should().ThrowAsync<EmergencyAccessException>();
        ex.Which.Code.Should().Be(EmergencyAccessErrorCodes.ReservedUsername);
        harness.Users.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserPage_excludes_the_designated_emergency_account()
    {
        var harness = Harness.Create();
        await harness.Service.CreateUserAsync("Operator", IdentitySource.Local, "operator", "unique-local-passphrase");
        harness.Users.Seed(new User(
            new UserId("emerg-1"),
            EmergencyAccessIdentity.DisplayName,
            [new IdentityLink(IdentitySource.Local, EmergencyAccessIdentity.LocalUsername, DateTimeOffset.UtcNow)],
            [new RoleId(BootstrapConstants.PlatformAdminRoleId)],
            IsBreakGlassEligible: true)
        { Username = EmergencyAccessIdentity.LocalUsername });

        var (page, total) = await harness.Service.GetUserPageAsync(0, 20);

        total.Should().Be(1);
        page.Should().ContainSingle(u => u.DisplayName == "Operator");
        page.Should().NotContain(u => u.IsBreakGlassEligible);
    }

    [Fact]
    public async Task GetUserById_hides_the_designated_emergency_account()
    {
        var harness = Harness.Create();
        harness.Users.Seed(new User(
            new UserId("emerg-1"),
            EmergencyAccessIdentity.DisplayName,
            [new IdentityLink(IdentitySource.Local, EmergencyAccessIdentity.LocalUsername, DateTimeOffset.UtcNow)],
            [new RoleId(BootstrapConstants.PlatformAdminRoleId)],
            IsBreakGlassEligible: true)
        { Username = EmergencyAccessIdentity.LocalUsername });

        var user = await harness.Service.GetUserByIdAsync(new UserId("emerg-1"));

        user.Should().BeNull();
    }

    [Fact]
    public async Task GetUserPage_paging_counts_only_ordinary_users()
    {
        var harness = Harness.Create();
        await harness.Service.CreateUserAsync("Alpha", IdentitySource.Local, "alpha", "unique-local-passphrase");
        await harness.Service.CreateUserAsync("Beta", IdentitySource.Local, "beta", "unique-local-passphrase");
        harness.Users.Seed(new User(
            new UserId("emerg-1"),
            EmergencyAccessIdentity.DisplayName,
            [new IdentityLink(IdentitySource.Local, EmergencyAccessIdentity.LocalUsername, DateTimeOffset.UtcNow)],
            [new RoleId(BootstrapConstants.PlatformAdminRoleId)],
            IsBreakGlassEligible: true)
        { Username = EmergencyAccessIdentity.LocalUsername });

        var (page, total) = await harness.Service.GetUserPageAsync(1, 1);

        total.Should().Be(2);
        page.Should().ContainSingle();
        page[0].IsBreakGlassEligible.Should().BeFalse();
    }

    [Fact]
    public async Task Ordinary_user_management_cannot_mutate_recovery_access()
    {
        var harness = Harness.Create();
        var emergency = new User(
            new UserId("emerg-1"),
            EmergencyAccessIdentity.DisplayName,
            [new IdentityLink(IdentitySource.Local, EmergencyAccessIdentity.LocalUsername, DateTimeOffset.UtcNow)],
            [new RoleId(BootstrapConstants.PlatformAdminRoleId)],
            IsBreakGlassEligible: true)
        { Username = EmergencyAccessIdentity.LocalUsername };
        harness.Users.Seed(emergency);
        harness.Credentials.Hashes["emerg-1"] = "h";
        harness.Credentials.Disabled["emerg-1"] = false;
        var userId = emergency.Id;
        var roleId = new RoleId(BootstrapConstants.PlatformAdminRoleId);

        (await AssertUnsafe(() => harness.Service.SetLocalPasswordAsync(userId, "unique-local-passphrase").AsTask()))
            .Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);
        (await AssertUnsafe(() => harness.Service.SetLocalCredentialDisabledAsync(userId, true).AsTask()))
            .Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);
        (await AssertUnsafe(() => harness.Service.AssignRoleAsync(userId, roleId).AsTask()))
            .Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);
        (await AssertUnsafe(() => harness.Service.RemoveRoleAsync(userId, roleId).AsTask()))
            .Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);
        (await AssertUnsafe(() => harness.Service.AddIdentityLinkAsync(userId, IdentitySource.Entra, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee").AsTask()))
            .Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);

        harness.Credentials.Disabled["emerg-1"].Should().BeFalse();
        harness.Roles.Assigned.Should().BeEmpty();
        return;

        static async Task<string> AssertUnsafe(Func<Task> act)
        {
            var ex = await act.Should().ThrowAsync<EmergencyAccessException>();
            return ex.Which.Code;
        }
    }

    private sealed class Harness
    {
        public RecordingUsers Users { get; } = new();
        public RecordingCredentials Credentials { get; } = new();
        public RecordingRoles Roles { get; } = new();
        public AuditRecording Audit { get; } = new();
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
                Options.Create(new LocalAuthOptions { MinPasswordLength = 8 }),
                auditedUnitOfWork: Audit,
                auditEvents: Audit);
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
        public void Seed(User user) => _users[user.Id.Value] = user;

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

        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default)
        {
            var ordinary = _users.Values.Where(u => !u.IsBreakGlassEligible).ToList();
            return ValueTask.FromResult<(IReadOnlyList<User>, int)>((ordinary.Skip(skip).Take(take).ToList(), ordinary.Count));
        }

        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingCredentials : ILocalCredentialRepository
    {
        public Dictionary<string, string> Hashes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, bool> Disabled { get; } = new(StringComparer.Ordinal);

        /// <summary>Fails the next credential write, as a database failure after the user row was added would.</summary>
        public bool FailNextSet { get; set; }

        public ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) =>
            Hashes.TryGetValue(userId.Value, out var hash)
                ? ValueTask.FromResult<LocalCredentialData?>(new LocalCredentialData(userId, hash, "s", "stub", Disabled[userId.Value]))
                : ValueTask.FromResult<LocalCredentialData?>(null);

        public ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default)
        {
            if (FailNextSet)
            {
                FailNextSet = false;
                throw new InvalidOperationException("Injected: the credential write failed.");
            }

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
