using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class EmergencyAccessServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 18, 0, 0, TimeSpan.Zero);
    private static readonly RoleId AdminRole = new(BootstrapConstants.PlatformAdminRoleId);

    [Fact]
    public async Task Platform_administrator_with_local_credential_can_be_designated()
    {
        var harness = Harness.WithAdmin("admin-1");

        await harness.Emergency.DesignateAsync(new UserId("admin-1"), new ActorId("cli"));

        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeTrue();
        harness.Audit.Actions.Should().Contain(EmergencyAccessAuditActionNames.Designated);
    }

    [Fact]
    public async Task Non_platform_administrator_cannot_be_designated()
    {
        var harness = Harness.WithOrdinaryUser();

        var act = () => harness.Emergency.DesignateAsync(new UserId("usr-1"), new ActorId("cli")).AsTask();

        var ex = await act.Should().ThrowAsync<EmergencyAccessException>();
        ex.Which.Code.Should().Be(EmergencyAccessErrorCodes.NotPlatformAdministrator);
        harness.Users.Get("usr-1")!.IsBreakGlassEligible.Should().BeFalse();
    }

    [Fact]
    public async Task Destructive_change_on_designated_account_is_rejected()
    {
        var harness = Harness.WithAdmin("admin-1");
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), new ActorId("cli"));

        var act = () => harness.Emergency.EnsureDestructiveChangeAllowedAsync(new UserId("admin-1"), "credential.disable").AsTask();

        var ex = await act.Should().ThrowAsync<EmergencyAccessException>();
        ex.Which.Code.Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);
        harness.Audit.Actions.Should().Contain(EmergencyAccessAuditActionNames.BlockedUnsafe);
    }

    [Fact]
    public async Task Revoke_without_confirm_fallback_is_rejected()
    {
        var harness = Harness.WithAdmin("admin-1");
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), new ActorId("cli"));

        var act = () => harness.Emergency.RevokeAsync(confirmFallback: false, new ActorId("cli")).AsTask();

        await act.Should().ThrowAsync<EmergencyAccessException>();
        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeTrue();
    }

    [Fact]
    public async Task Posture_is_unhealthy_until_mfa_and_recovery_codes_exist()
    {
        var harness = Harness.WithAdmin("admin-1");
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), new ActorId("cli"));

        var before = await harness.Emergency.GetPostureAsync();
        before.IsHealthy.Should().BeFalse();
        before.FallbackSafeguardRequired.Should().BeTrue();

        harness.Mfa.Enrolled["admin-1"] = true;
        harness.Mfa.UnusedCodes["admin-1"] = 10;
        var after = await harness.Emergency.GetPostureAsync();
        after.IsHealthy.Should().BeTrue();
        after.FallbackSafeguardRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Provision_creates_a_designated_platform_administrator()
    {
        var harness = Harness.Empty();
        harness.Roles.RoleExists = true;

        var userId = await harness.Emergency.ProvisionAsync("quiet-maple-lantern-harbor", new ActorId("cli"));

        var user = harness.Users.Get(userId.Value)!;
        user.IsBreakGlassEligible.Should().BeTrue();
        user.DisplayName.Should().Be(EmergencyAccessIdentity.DisplayName);
        user.IdentityLinks.Should().ContainSingle(l =>
            l.Source == IdentitySource.Local && l.ExternalId == EmergencyAccessIdentity.LocalUsername);
        user.AssignedRoleIds.Should().Contain(AdminRole);
        harness.Credentials.Disabled[userId.Value].Should().BeFalse();
        harness.Audit.Actions.Should().Contain(EmergencyAccessAuditActionNames.Provisioned);
        harness.Audit.Details.Should().NotContain("quiet-maple-lantern-harbor");
    }

    [Fact]
    public async Task Provision_rejects_a_weak_password_without_creating_the_account()
    {
        var harness = Harness.Empty();
        harness.Roles.RoleExists = true;

        var act = () => harness.Emergency.ProvisionAsync("password", new ActorId("cli")).AsTask();

        await act.Should().ThrowAsync<PasswordPolicyException>();
        harness.Users.All.Should().BeEmpty();
        harness.Credentials.Disabled.Should().BeEmpty();
    }

    [Fact]
    public async Task Provision_is_idempotent_when_recovery_access_already_exists()
    {
        var harness = Harness.Empty();
        harness.Roles.RoleExists = true;
        var first = await harness.Emergency.ProvisionAsync("quiet-maple-lantern-harbor", new ActorId("cli"));

        var second = await harness.Emergency.ProvisionAsync("another-password", new ActorId("cli"));

        second.Should().Be(first);
        harness.Users.All.Should().ContainSingle(u => u.IsBreakGlassEligible);
    }

    [Fact]
    public async Task EnsureCanonical_upgrades_existing_designated_user_in_place()
    {
        var harness = Harness.WithAdmin("emerg-1");
        harness.Users.Seed(Admin("emerg-1", designated: true, username: "oldglass", displayName: "Emergency Administrator"));
        harness.Credentials.Seed("emerg-1", disabled: false);
        harness.Mfa.Enrolled["emerg-1"] = true;
        harness.Mfa.UnusedCodes["emerg-1"] = 9;

        await harness.Emergency.EnsureCanonicalIdentityAsync();

        var user = harness.Users.Get("emerg-1")!;
        user.Id.Value.Should().Be("emerg-1");
        user.IsBreakGlassEligible.Should().BeTrue();
        user.DisplayName.Should().Be(EmergencyAccessIdentity.DisplayName);
        EmergencyAccessIdentity.LocalUsernameOf(user).Should().Be(EmergencyAccessIdentity.LocalUsername);
        harness.Credentials.Disabled["emerg-1"].Should().BeFalse();
        harness.Mfa.Enrolled["emerg-1"].Should().BeTrue();
        harness.Mfa.UnusedCodes["emerg-1"].Should().Be(9);
        harness.Audit.Actions.Should().Contain(EmergencyAccessAuditActionNames.Canonicalized);
        harness.Audit.Details.Should().NotContain("oldglass-secret");
    }

    [Fact]
    public async Task Provision_fails_clearly_when_another_user_already_owns_breakglass()
    {
        var harness = Harness.Empty();
        harness.Roles.RoleExists = true;
        harness.Users.Seed(new User(
            new UserId("usr-other"),
            "Other",
            [new IdentityLink(IdentitySource.Local, EmergencyAccessIdentity.LocalUsername, Now)],
            [],
            false));

        var act = () => harness.Emergency.ProvisionAsync("quiet-maple-lantern-harbor", new ActorId("cli")).AsTask();

        var ex = await act.Should().ThrowAsync<EmergencyAccessException>();
        ex.Which.Code.Should().Be(EmergencyAccessErrorCodes.UsernameConflict);
        harness.Users.All.Should().NotContain(u => u.IsBreakGlassEligible);
        EmergencyAccessIdentity.LocalUsernameOf(harness.Users.Get("usr-other")!).Should().Be(EmergencyAccessIdentity.LocalUsername);
    }

    [Fact]
    public async Task EnsureCanonical_leaves_recovery_path_unchanged_when_breakglass_is_owned_by_another_user()
    {
        var harness = Harness.WithAdmin("emerg-1");
        harness.Users.Seed(Admin("emerg-1", designated: true, username: "oldglass", displayName: "Emergency Administrator"));
        harness.Users.Seed(new User(
            new UserId("usr-other"),
            "Other",
            [new IdentityLink(IdentitySource.Local, EmergencyAccessIdentity.LocalUsername, Now)],
            [],
            false));
        harness.Credentials.Seed("emerg-1", disabled: false);
        harness.Mfa.Enrolled["emerg-1"] = true;
        harness.Mfa.UnusedCodes["emerg-1"] = 4;

        var act = () => harness.Emergency.EnsureCanonicalIdentityAsync().AsTask();

        var ex = await act.Should().ThrowAsync<EmergencyAccessException>();
        ex.Which.Code.Should().Be(EmergencyAccessErrorCodes.UsernameConflict);
        var designated = harness.Users.Get("emerg-1")!;
        designated.IsBreakGlassEligible.Should().BeTrue();
        EmergencyAccessIdentity.LocalUsernameOf(designated).Should().Be("oldglass");
        designated.DisplayName.Should().Be("Emergency Administrator");
        harness.Mfa.UnusedCodes["emerg-1"].Should().Be(4);
    }

    [Fact]
    public async Task Designating_a_second_user_is_rejected_while_canonical_username_is_held_by_the_current_emergency_account()
    {
        var harness = Harness.WithAdmin("admin-1");
        harness.Users.Seed(Admin("admin-2", designated: false));
        harness.Credentials.Seed("admin-2", disabled: false);
        harness.Roles.UserIds.Add(new UserId("admin-2"));
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), new ActorId("cli"));

        var act = () => harness.Emergency.DesignateAsync(new UserId("admin-2"), new ActorId("cli")).AsTask();

        var ex = await act.Should().ThrowAsync<EmergencyAccessException>();
        ex.Which.Code.Should().Be(EmergencyAccessErrorCodes.UsernameConflict);
        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeTrue();
        harness.Users.Get("admin-2")!.IsBreakGlassEligible.Should().BeFalse();
    }

    private static User Admin(string id, bool designated, string? username = null, string? displayName = null) => new(
        new UserId(id),
        displayName ?? "Platform Administrator",
        [new IdentityLink(IdentitySource.Local, username ?? id, Now)],
        [AdminRole],
        designated);

    private sealed class Harness
    {
        public RecordingUsers Users { get; } = new();
        public RecordingCredentials Credentials { get; } = new();
        public RecordingRoles Roles { get; } = new();
        public FakeMfa Mfa { get; } = new();
        public RecordingAudit Audit { get; } = new();
        public EmergencyAccessService Emergency { get; }

        private Harness()
        {
            Emergency = new EmergencyAccessService(
                Users,
                Roles,
                Credentials,
                new LocalPasswordHasher(),
                Mfa,
                Audit,
                TimeProvider.System,
                NullLogger<EmergencyAccessService>.Instance);
        }

        public static Harness Empty() => new();

        public static Harness WithAdmin(string id)
        {
            var harness = new Harness();
            harness.Users.Seed(Admin(id, designated: false));
            harness.Credentials.Seed(id, disabled: false);
            harness.Roles.UserIds.Add(new UserId(id));
            harness.Roles.RoleExists = true;
            return harness;
        }

        public static Harness WithOrdinaryUser()
        {
            var harness = new Harness();
            harness.Users.Seed(new User(
                new UserId("usr-1"),
                "Operator",
                [new IdentityLink(IdentitySource.Local, "operator", Now)],
                [],
                false));
            harness.Credentials.Seed("usr-1", disabled: false);
            return harness;
        }
    }

    private sealed class RecordingUsers : IUserRepository
    {
        private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);
        public void Seed(User user) => _users[user.Id.Value] = user;
        public User? Get(string id) => _users.GetValueOrDefault(id);
        public IReadOnlyCollection<User> All => _users.Values;
        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.Count);
        public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.GetValueOrDefault(id.Value));
        public ValueTask<User?> FindDesignatedEmergencyUserAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u => u.IsBreakGlassEligible));
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u => u.IdentityLinks.Any(l => l.Source == source && l.ExternalId == externalId)));
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
        public ValueTask ApplyCanonicalLocalIdentityAsync(
            UserId userId,
            string displayName,
            string currentLocalUsername,
            string newLocalUsername,
            CancellationToken cancellationToken = default)
        {
            var user = _users[userId.Value];
            var owner = _users.Values.FirstOrDefault(u =>
                u.IdentityLinks.Any(l => l.Source == IdentitySource.Local && l.ExternalId == newLocalUsername));
            if (owner is not null && owner.Id.Value != userId.Value)
                throw new InvalidOperationException("An identity link with this source and external id already exists.");
            var links = user.IdentityLinks
                .Select(l =>
                    l.Source == IdentitySource.Local && l.ExternalId == currentLocalUsername
                        ? l with { ExternalId = newLocalUsername }
                        : l)
                .ToList();
            _users[userId.Value] = user with { DisplayName = displayName, IdentityLinks = links };
            return ValueTask.CompletedTask;
        }
        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) => ValueTask.FromResult<(IReadOnlyList<User>, int)>(([], 0));
        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class RecordingCredentials : ILocalCredentialRepository
    {
        public Dictionary<string, bool> Disabled { get; } = new(StringComparer.Ordinal);
        public void Seed(string userId, bool disabled) => Disabled[userId] = disabled;
        public ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) =>
            Disabled.ContainsKey(userId.Value)
                ? ValueTask.FromResult<LocalCredentialData?>(new LocalCredentialData(userId, "hash", "salt", "stub", Disabled[userId.Value]))
                : ValueTask.FromResult<LocalCredentialData?>(null);
        public ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default)
        {
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
        public List<UserId> UserIds { get; } = [];
        public bool RoleExists { get; set; }
        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(RoleExists ? new Role(roleId, "Platform Administrator", null, [], true) : null);
        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult(UserIds.Count);
        public ValueTask<IReadOnlyList<UserId>> ListUserIdsWithRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<UserId>>(UserIds);
        public ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FakeMfa : INativeMfaService
    {
        public Dictionary<string, bool> Enrolled { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> UnusedCodes { get; } = new(StringComparer.Ordinal);
        public ValueTask<bool> IsEnrolledAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Enrolled.GetValueOrDefault(userId.Value));
        public ValueTask<bool> IsRequiredAsync(User user, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(user.IsBreakGlassEligible || Enrolled.GetValueOrDefault(user.Id.Value));
        public ValueTask<NativeMfaEnrollmentChallenge> BeginEnrollmentAsync(UserId userId, string accountName, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public ValueTask<IReadOnlyList<string>> CompleteEnrollmentAsync(UserId userId, string totpCode, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public ValueTask<bool> VerifyTotpAsync(UserId userId, string totpCode, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<bool> ConsumeRecoveryCodeAsync(UserId userId, string recoveryCode, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(UserId userId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public ValueTask ResetAsync(UserId userId, ActorId? actorId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<int> CountUnusedRecoveryCodesAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(UnusedCodes.GetValueOrDefault(userId.Value));
    }

    private sealed class RecordingAudit : IAuditService
    {
        public List<string> Actions { get; } = [];
        public List<string> Details { get; } = [];
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Actions.Add(record.Action);
            if (record.Details is not null)
                Details.Add(record.Details);
            return ValueTask.CompletedTask;
        }
        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(new AuditPage([], null));
        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => ValueTask.FromResult<AuditRecord?>(null);
    }
}
