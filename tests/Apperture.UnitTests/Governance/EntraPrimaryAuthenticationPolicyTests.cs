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

public sealed class EntraPrimaryAuthenticationPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly RoleId AdminRole = new(BootstrapConstants.PlatformAdminRoleId);

    [Fact]
    public async Task Ordinary_user_local_credential_is_disabled_and_hash_is_retained()
    {
        var harness = Harness.OrdinaryUser();

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("usr-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        harness.Credentials.Disabled["usr-1"].Should().BeTrue();
        harness.Credentials.Hashes["usr-1"].Should().Be("hash-original");
        harness.Audit.Actions.Should().Contain(StrongAuthenticationAuditActionNames.LocalDisabledEntraPrimary);
    }

    [Fact]
    public async Task Already_disabled_is_idempotent_without_another_audit()
    {
        var harness = Harness.OrdinaryUser(alreadyDisabled: true);

        var first = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("usr-1"));
        harness.Audit.Actions.Clear();
        var second = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("usr-1"));

        first.Should().Be(EntraPrimaryLocalDisableOutcome.AlreadyDisabled);
        second.Should().Be(EntraPrimaryLocalDisableOutcome.AlreadyDisabled);
        harness.Audit.Actions.Should().BeEmpty();
        harness.Credentials.Hashes["usr-1"].Should().Be("hash-original");
    }

    [Fact]
    public async Task Last_entra_independent_platform_administrator_is_not_auto_disabled()
    {
        var harness = Harness.SolePlatformAdministrator();

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
        harness.Audit.Actions.Should().Contain(StrongAuthenticationAuditActionNames.LocalDisableDeferredLastAdmin);
    }

    [Fact]
    public async Task Designated_emergency_account_is_never_auto_disabled()
    {
        var harness = Harness.SolePlatformAdministrator();
        harness.Users.Seed(new User(
            new UserId("admin-1"),
            "Emergency Administrator",
            [
                new IdentityLink(IdentitySource.Local, "admin", Now),
                new IdentityLink(IdentitySource.Entra, "33333333-3333-3333-3333-333333333333", Now)
            ],
            [AdminRole],
            IsBreakGlassEligible: true));

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.ProtectedEmergencyAccount);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
        harness.Audit.Actions.Should().Contain(EmergencyAccessAuditActionNames.LocalProtected);
    }

    [Fact]
    public async Task Healthy_emergency_access_allows_ordinary_last_admin_local_disable()
    {
        var harness = Harness.SolePlatformAdministrator();
        harness.Emergency.Healthy = true;
        harness.Users.Seed(new User(
            new UserId("emerg-1"),
            "Emergency Administrator",
            [new IdentityLink(IdentitySource.Local, "breakglass", Now)],
            [AdminRole],
            IsBreakGlassEligible: true));
        harness.Credentials.Seed("emerg-1", "hash-e", disabled: false);
        harness.Roles.UserIds.Add(new UserId("emerg-1"));

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        harness.Credentials.Disabled["admin-1"].Should().BeTrue();
        harness.Credentials.Disabled["emerg-1"].Should().BeFalse();
        harness.Audit.Actions.Should().Contain(StrongAuthenticationAuditActionNames.LocalDisabledEntraPrimary);
        harness.Audit.Actions.Should().NotContain(StrongAuthenticationAuditActionNames.LocalDisableDeferredLastAdmin);
    }

    [Fact]
    public async Task Incomplete_emergency_account_does_not_lift_last_admin_safeguard()
    {
        var harness = Harness.SolePlatformAdministrator();
        harness.Emergency.Healthy = false;
        harness.Users.Seed(new User(
            new UserId("emerg-1"),
            "Emergency Administrator",
            [new IdentityLink(IdentitySource.Local, "breakglass", Now)],
            [AdminRole],
            IsBreakGlassEligible: true));
        harness.Credentials.Seed("emerg-1", "hash-e", disabled: false);
        harness.Roles.UserIds.Add(new UserId("emerg-1"));

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
    }

    [Fact]
    public async Task Describe_marks_designated_emergency_local_policy()
    {
        var harness = Harness.SolePlatformAdministrator();
        var user = new User(
            new UserId("emerg-1"),
            "Emergency Administrator",
            [new IdentityLink(IdentitySource.Local, "breakglass", Now)],
            [AdminRole],
            IsBreakGlassEligible: true);
        harness.Users.Seed(user);
        harness.Credentials.Seed("emerg-1", "hash-e", disabled: false);
        var cred = await harness.Credentials.GetByUserIdAsync(new UserId("emerg-1"));

        var presentation = await harness.Policy.DescribeAsync(user, cred);

        presentation.LocalSignInPolicy.Should().Be(AuthenticationMethodStatuses.LocalEmergencyAccess);
        presentation.EmergencyAccessAccount.Should().BeTrue();
        presentation.RecoverySafeguardActive.Should().BeFalse();
    }

    [Fact]
    public async Task Second_local_platform_administrator_allows_policy_to_proceed()
    {
        var harness = Harness.TwoLocalPlatformAdministrators();

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        harness.Credentials.Disabled["admin-1"].Should().BeTrue();
        harness.Credentials.Disabled["admin-2"].Should().BeFalse();
    }

    [Fact]
    public async Task Ordinary_user_is_not_preserved_because_emergency_access_is_absent()
    {
        var harness = Harness.OrdinaryUser();

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("usr-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
    }

    [Fact]
    public async Task Describe_marks_the_recovery_safeguard_for_the_last_local_admin()
    {
        var harness = Harness.SolePlatformAdministrator();
        var user = await harness.Users.GetByIdAsync(new UserId("admin-1"));
        var cred = await harness.Credentials.GetByUserIdAsync(new UserId("admin-1"));

        var presentation = await harness.Policy.DescribeAsync(user!, cred);

        presentation.LocalSignInPolicy.Should().Be(AuthenticationMethodStatuses.LocalRecoverySafeguard);
        presentation.RecoverySafeguardActive.Should().BeTrue();
        presentation.EntraAssociationStatus.Should().Be(AuthenticationMethodStatuses.EntraProven);
    }

    [Fact]
    public async Task Previously_deferred_last_admin_is_disabled_after_another_independent_admin_exists()
    {
        var harness = Harness.SolePlatformAdministrator();

        var deferred = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));
        deferred.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();

        harness.Users.Seed(new User(
            new UserId("admin-2"),
            "Second Administrator",
            [new IdentityLink(IdentitySource.Local, "admin2", Now)],
            [AdminRole],
            false));
        harness.Credentials.Seed("admin-2", "hash-other", disabled: false);
        harness.Roles.UserIds.Add(new UserId("admin-2"));
        harness.Audit.Actions.Clear();

        var reevaluated = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        reevaluated.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        harness.Credentials.Disabled["admin-1"].Should().BeTrue();
        harness.Credentials.Disabled["admin-2"].Should().BeFalse();
        harness.Credentials.Hashes["admin-1"].Should().Be("hash-original");
        harness.Audit.Actions.Should().Contain(StrongAuthenticationAuditActionNames.LocalDisabledEntraPrimary);
        harness.Audit.Actions.Should().NotContain(StrongAuthenticationAuditActionNames.LocalDisableDeferredLastAdmin);
    }

    [Fact]
    public async Task Subsequent_apply_while_still_the_last_independent_admin_remains_deferred()
    {
        var harness = Harness.SolePlatformAdministrator();

        var first = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));
        var second = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        first.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        second.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
    }

    private sealed class Harness
    {
        public RecordingUsers Users { get; } = new();
        public RecordingCredentials Credentials { get; } = new();
        public RecordingRoles Roles { get; } = new();
        public RecordingAudit Audit { get; } = new();
        public FakeEmergency Emergency { get; } = new();
        public EntraPrimaryAuthenticationPolicy Policy { get; }

        private Harness()
        {
            Policy = new EntraPrimaryAuthenticationPolicy(
                Users,
                Credentials,
                Roles,
                Audit,
                TimeProvider.System,
                NullLogger<EntraPrimaryAuthenticationPolicy>.Instance,
                Emergency);
        }

        public static Harness OrdinaryUser(bool alreadyDisabled = false)
        {
            var harness = new Harness();
            harness.Users.Seed(new User(
                new UserId("usr-1"),
                "Operator",
                [
                    new IdentityLink(IdentitySource.Local, "operator", Now),
                    new IdentityLink(IdentitySource.Entra, "33333333-3333-3333-3333-333333333333", Now)
                ],
                [],
                false));
            harness.Credentials.Seed("usr-1", "hash-original", alreadyDisabled);
            return harness;
        }

        public static Harness SolePlatformAdministrator()
        {
            var harness = new Harness();
            harness.Users.Seed(new User(
                new UserId("admin-1"),
                "Platform Administrator",
                [
                    new IdentityLink(IdentitySource.Local, "admin", Now),
                    new IdentityLink(IdentitySource.Entra, "33333333-3333-3333-3333-333333333333", Now)
                ],
                [AdminRole],
                false));
            harness.Credentials.Seed("admin-1", "hash-original", disabled: false);
            harness.Roles.UserIds.Add(new UserId("admin-1"));
            return harness;
        }

        public static Harness TwoLocalPlatformAdministrators()
        {
            var harness = SolePlatformAdministrator();
            harness.Users.Seed(new User(
                new UserId("admin-2"),
                "Second Administrator",
                [new IdentityLink(IdentitySource.Local, "admin2", Now)],
                [AdminRole],
                false));
            harness.Credentials.Seed("admin-2", "hash-other", disabled: false);
            harness.Roles.UserIds.Add(new UserId("admin-2"));
            return harness;
        }
    }

    private sealed class RecordingUsers : IUserRepository
    {
        private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);
        public void Seed(User user) => _users[user.Id.Value] = user;
        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.Count);
        public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.GetValueOrDefault(id.Value));
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u => u.IdentityLinks.Any(l => l.Source == source && l.ExternalId == externalId)));
        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) => ValueTask.FromResult<(IReadOnlyList<User>, int)>(([], 0));
        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CompleteEntraLinkProofAsync(string externalId, DateTimeOffset provenAt, string? tenantIdWhenMissing, string? registrationIdWhenMissing, string? providerSubject, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<User?> FindDesignatedEmergencyUserAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u => u.IsBreakGlassEligible));
    }

    private sealed class RecordingCredentials : ILocalCredentialRepository
    {
        public Dictionary<string, string> Hashes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, bool> Disabled { get; } = new(StringComparer.Ordinal);

        public void Seed(string userId, string hash, bool disabled)
        {
            Hashes[userId] = hash;
            Disabled[userId] = disabled;
        }

        public ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) =>
            Hashes.TryGetValue(userId.Value, out var hash)
                ? ValueTask.FromResult<LocalCredentialData?>(new LocalCredentialData(userId, hash, "salt", "stub", Disabled[userId.Value]))
                : ValueTask.FromResult<LocalCredentialData?>(null);

        public ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default)
        {
            Hashes[userId.Value] = passwordHash;
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
        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult<Role?>(null);
        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult(UserIds.Count);
        public ValueTask<IReadOnlyList<UserId>> ListUserIdsWithRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<UserId>>(UserIds);
        public ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class RecordingAudit : IAuditService
    {
        public List<string> Actions { get; } = [];
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Actions.Add(record.Action);
            return ValueTask.CompletedTask;
        }
        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(new AuditPage([], null));
        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => ValueTask.FromResult<AuditRecord?>(null);
    }

    private sealed class FakeEmergency : IEmergencyAccessService
    {
        public bool Healthy { get; set; }

        public ValueTask<EmergencyAccessPosture> GetPostureAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EmergencyAccessPosture(
                Healthy,
                FallbackSafeguardRequired: !Healthy,
                Healthy ? "emerg-1" : null,
                Healthy ? "Emergency Administrator" : null,
                Healthy,
                Healthy,
                Healthy,
                Healthy ? 10 : 0));

        public ValueTask DesignateAsync(UserId userId, ActorId? actorId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<UserId> ProvisionAsync(string password, ActorId? actorId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new UserId("emerg-1"));
        public ValueTask EnsureCanonicalIdentityAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RevokeAsync(bool confirmFallback, ActorId? actorId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SetLocalPasswordAsync(UserId userId, string password, ActorId? actorId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<bool> IsDesignatedEmergencyUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(string.Equals(userId.Value, "emerg-1", StringComparison.Ordinal));
        public ValueTask EnsureDestructiveChangeAllowedAsync(UserId userId, string operation, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
