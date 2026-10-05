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

/// <summary>
/// Administrator rename: <c>user.manage</c>, the policy and ownership rules of <see cref="UsernameService"/>, the audit row,
/// and the refusal for Recovery Access. The ledger itself is proven against SQL Server elsewhere.
/// </summary>
public sealed class UserManagementServiceRenameUsernameTests
{
    private const string TargetId = "00112233445566778899aabb";

    [Fact]
    public async Task Rename_without_user_manage_is_rejected_and_changes_nothing()
    {
        var harness = Harness.Create(allowUserManage: false);

        var act = () => harness.Service.RenameUsernameAsync(new UserId(TargetId), "alice.renamed").AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        harness.Users.Changes.Should().BeEmpty();
        harness.Audit.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Rename_to_a_new_name_assigns_it_and_audits_the_previous_and_new_names()
    {
        var harness = Harness.Create();

        var result = await harness.Service.RenameUsernameAsync(new UserId(TargetId), "  Alice.Renamed ");

        result.Outcome.Should().Be(UsernameChangeOutcome.Assigned);
        result.PreviousUsername.Should().Be("alice");
        result.Username.Should().Be("alice.renamed", "the stored form is the normalized one");
        harness.Users.Changes.Should().ContainSingle().Which.Should().Be((TargetId, "alice", "alice.renamed", UsernameRetiredReasons.Rename));

        var audit = harness.Audit.Records.Should().ContainSingle().Which;
        audit.Action.Should().Be("governance.user.username.changed");
        audit.ActorId.Value.Should().Be("admin");
        audit.Resource!.ResourceType.Should().Be(ResourceTypeConstants.User);
        audit.Details.Should().Contain("previousUsername=alice").And.Contain("newUsername=alice.renamed");
    }

    [Fact]
    public async Task Rename_to_one_of_the_users_own_retired_names_is_a_reclaim()
    {
        var harness = Harness.Create();
        harness.Users.OwnRetired.Add("alice.old");

        var result = await harness.Service.RenameUsernameAsync(new UserId(TargetId), "alice.old");

        result.Outcome.Should().Be(UsernameChangeOutcome.Reclaimed);
        harness.Audit.Records.Should().ContainSingle(a => a.Action == "governance.user.username.changed");
    }

    [Fact]
    public async Task Rename_to_the_current_name_changes_nothing_and_writes_no_audit_row()
    {
        var harness = Harness.Create();

        var result = await harness.Service.RenameUsernameAsync(new UserId(TargetId), "ALICE");

        result.Outcome.Should().Be(UsernameChangeOutcome.Unchanged);
        result.Username.Should().Be("alice");
        harness.Users.Changes.Should().BeEmpty();
        harness.Audit.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Rename_to_a_name_another_user_holds_or_held_is_taken_and_not_audited_as_changed()
    {
        var harness = Harness.Create();
        harness.Users.OtherOwners.Add("bob");

        var act = () => harness.Service.RenameUsernameAsync(new UserId(TargetId), "bob").AsTask();

        (await act.Should().ThrowAsync<UsernameException>()).Which.Code.Should().Be(UsernameErrorCodes.Taken);
        harness.Audit.Records.Should().NotContain(a => a.Action == "governance.user.username.changed");
    }

    [Theory]
    [InlineData("jane@contoso.com", UsernameErrorCodes.Invalid)]
    [InlineData("ab", UsernameErrorCodes.Invalid)]
    [InlineData("alice-", UsernameErrorCodes.Invalid)]
    [InlineData("001122334455667788990011", UsernameErrorCodes.Invalid)]
    [InlineData("system", UsernameErrorCodes.Reserved)]
    [InlineData("breakglass", UsernameErrorCodes.Reserved)]
    public async Task Rename_requires_the_target_to_satisfy_the_current_policy(string requested, string code)
    {
        var harness = Harness.Create();

        var act = () => harness.Service.RenameUsernameAsync(new UserId(TargetId), requested).AsTask();

        (await act.Should().ThrowAsync<UsernameException>()).Which.Code.Should().Be(code);
        harness.Users.Changes.Should().BeEmpty();
        harness.Audit.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Rename_refuses_the_system_managed_recovery_access_user()
    {
        var harness = Harness.Create();
        harness.Users.Seed(new User(new UserId("recoveryaccount0000000001"), "Recovery Access", [], [], IsBreakGlassEligible: true) { Username = "breakglass" });

        var act = () => harness.Service.RenameUsernameAsync(new UserId("recoveryaccount0000000001"), "someone.else").AsTask();

        (await act.Should().ThrowAsync<EmergencyAccessException>()).Which.Code.Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);
        harness.Users.Changes.Should().BeEmpty();
        harness.Audit.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Rename_of_a_missing_user_reports_not_found()
    {
        var harness = Harness.Create();

        var act = () => harness.Service.RenameUsernameAsync(new UserId("ffffffffffffffffffffffff"), "someone.else").AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        harness.Audit.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Rename_touches_no_credential_role_or_identity_link_state_beyond_the_username_change()
    {
        var harness = Harness.Create();

        await harness.Service.RenameUsernameAsync(new UserId(TargetId), "alice.renamed");

        harness.Users.OtherMutations.Should().Be(0, "only ChangeUsernameAsync moves the username and the Local link together");
        harness.Roles.Assigned.Should().BeEmpty();
        harness.Credentials.Touched.Should().BeFalse();
    }

    private sealed class Harness
    {
        public InMemoryUsers Users { get; } = new();
        public RecordingAudit Audit { get; } = new();
        public RecordingRoles Roles { get; } = new();
        public RecordingCredentials Credentials { get; } = new();
        public UserManagementService Service { get; }

        private Harness(bool allowUserManage)
        {
            Users.Seed(new User(
                new UserId(TargetId),
                "Alice Example",
                [new IdentityLink(IdentitySource.Local, "alice", LastAuthenticatedAt: null)],
                [],
                IsBreakGlassEligible: false) { Username = "alice" });
            Service = new UserManagementService(
                new StubPrincipal(),
                new StubResolver(),
                new UserManageOnlyAuth(allowUserManage),
                Users,
                Roles,
                new InMemoryResourceDescriptorRegistry(),
                Credentials,
                new StubHasher(),
                Audit,
                Options.Create(new LocalAuthOptions { MinPasswordLength = 8 }));
        }

        public static Harness Create(bool allowUserManage = true) => new(allowUserManage);
    }

    private sealed class UserManageOnlyAuth(bool allowUserManage) : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(permission.Action != ActionConstants.UserManage || allowUserManage);
    }

    private sealed class InMemoryUsers : IUserRepository
    {
        private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);

        public List<(string UserId, string From, string To, string Reason)> Changes { get; } = [];
        public HashSet<string> OtherOwners { get; } = new(StringComparer.Ordinal);
        public HashSet<string> OwnRetired { get; } = new(StringComparer.Ordinal);
        public int OtherMutations { get; private set; }

        public void Seed(User user) => _users[user.Id.Value] = user;

        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.Count);

        public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.GetValueOrDefault(id.Value));

        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u => u.IdentityLinks.Any(l => l.Source == source && l.ExternalId == externalId)));

        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default)
        {
            OtherMutations++;
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default)
        {
            OtherMutations++;
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<(IReadOnlyList<User>, int)>(([], 0));

        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default)
        {
            OtherMutations++;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default)
        {
            OtherMutations++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<UsernameChangeOutcome> ChangeUsernameAsync(
            UserId userId,
            string expectedCurrentUsername,
            string newUsername,
            string retiredReason,
            string? newDisplayName = null,
            CancellationToken cancellationToken = default)
        {
            if (OtherOwners.Contains(newUsername))
                throw new UsernameException(UsernameErrorCodes.Taken, "That username belongs, or once belonged, to another user.");

            Changes.Add((userId.Value, expectedCurrentUsername, newUsername, retiredReason));
            _users[userId.Value] = _users[userId.Value] with { Username = newUsername };
            return ValueTask.FromResult(OwnRetired.Contains(newUsername) ? UsernameChangeOutcome.Reclaimed : UsernameChangeOutcome.Assigned);
        }
    }

    private sealed class RecordingAudit : IAuditService
    {
        public List<AuditRecord> Records { get; } = [];

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage([], null));

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AuditRecord?>(null);
    }

    private sealed class RecordingRoles : IRoleRepository
    {
        public List<(string UserId, string RoleId)> Assigned { get; } = [];

        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult<Role?>(null);

        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);

        public ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default)
        {
            Assigned.Add((userId.Value, roleId.Value));
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingCredentials : ILocalCredentialRepository
    {
        public bool Touched { get; private set; }

        public ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<LocalCredentialData?>(null);

        public ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default)
        {
            Touched = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask SetDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default)
        {
            Touched = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubHasher : ILocalPasswordHasher
    {
        public string AlgorithmName => "stub";
        public (string Hash, string Salt) HashPassword(string password) => ("h", "s");
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
