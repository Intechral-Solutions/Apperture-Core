using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class EmergencyAccessServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 18, 0, 0, TimeSpan.Zero);
    private static readonly RoleId AdminRole = new(BootstrapConstants.PlatformAdminRoleId);
    private static readonly ActorId Cli = new(EmergencyAccessService.CliActorId);

    [Fact]
    public async Task Platform_administrator_with_local_credential_can_be_designated()
    {
        var harness = Harness.WithAdmin("admin-1");

        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);

        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeTrue();

        // AUD-1.2: one unit. The command line is the actor and the account is the target (the earlier rows could name the
        // account itself); the canonical identity it took commits in the same unit as a secondary row.
        var operation = harness.Recorder.Operations.Should().ContainSingle().Which;
        operation.Operation.Actor.Should().Be(AuditActor.EmergencyCli);
        operation.Staged.Select(e => e.Action).Should().BeEquivalentTo(
            [AuditActionNames.RecoveryAccessCanonicalized, AuditActionNames.RecoveryAccessDesignated]);
        var designated = harness.Recorder.Single(AuditActionNames.RecoveryAccessDesignated);
        designated.Target!.Id.Should().Be("admin-1");
        designated.Fields.Should().NotContainKey("previousUserId", "nobody was designated before");
    }

    [Fact]
    public async Task Designating_a_new_account_demotes_the_previous_one_and_records_it()
    {
        var harness = Harness.WithAdmin("admin-1");
        harness.Users.Seed(Admin("old-ra", designated: true, username: "oldglass"));
        harness.Credentials.Seed("old-ra", disabled: false);

        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);

        harness.Users.Get("old-ra")!.IsBreakGlassEligible.Should().BeFalse();
        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeTrue();
        harness.Recorder.Single(AuditActionNames.RecoveryAccessDesignated).Fields["previousUserId"].Should().Be("old-ra");
    }

    [Fact]
    public async Task Re_designating_the_designated_canonical_account_changes_nothing_and_records_nothing()
    {
        var harness = Harness.WithAdmin("admin-1");
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);

        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);

        harness.Recorder.Operations.Should().HaveCount(2);
        harness.Recorder.Operations[1].NoOp.Should().BeTrue();
        harness.Recorder.Actions.Should().ContainSingle(a => a == AuditActionNames.RecoveryAccessDesignated);
    }

    [Fact]
    public async Task Non_platform_administrator_cannot_be_designated()
    {
        var harness = Harness.WithOrdinaryUser();

        var act = () => harness.Emergency.DesignateAsync(new UserId("usr-1"), Cli).AsTask();

        var ex = await act.Should().ThrowAsync<EmergencyAccessException>();
        ex.Which.Code.Should().Be(EmergencyAccessErrorCodes.NotPlatformAdministrator);
        harness.Users.Get("usr-1")!.IsBreakGlassEligible.Should().BeFalse();
        harness.Recorder.Staged.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_the_designated_account_is_reported_as_the_recovery_path()
    {
        // The safety check a destructive change runs inside its own transaction (the refusal row is the caller's).
        var harness = Harness.WithAdmin("admin-1");
        harness.Users.Seed(Admin("admin-2", designated: false));
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);

        (await harness.Emergency.IsDesignatedEmergencyUserAsync(new UserId("admin-1"))).Should().BeTrue();
        (await harness.Emergency.IsDesignatedEmergencyUserAsync(new UserId("admin-2"))).Should().BeFalse();
    }

    [Fact]
    public async Task Revoke_without_confirm_fallback_is_rejected()
    {
        var harness = Harness.WithAdmin("admin-1");
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);

        var act = () => harness.Emergency.RevokeAsync(confirmFallback: false, Cli).AsTask();

        await act.Should().ThrowAsync<EmergencyAccessException>();
        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeTrue();
        harness.Recorder.Actions.Should().NotContain(AuditActionNames.RecoveryAccessRevoked);
    }

    [Fact]
    public async Task Revoke_with_confirm_fallback_commits_with_its_row_attributed_to_the_command_line()
    {
        var harness = Harness.WithAdmin("admin-1");
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);

        await harness.Emergency.RevokeAsync(confirmFallback: true, Cli);

        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeFalse();
        var operation = harness.Recorder.Operations.Last();
        operation.Operation.Actor.Should().Be(AuditActor.EmergencyCli);
        operation.Staged.Should().ContainSingle().Which.Target!.Id.Should().Be("admin-1");
    }

    [Fact]
    public async Task Command_line_password_set_commits_with_its_row_and_never_records_the_password()
    {
        var harness = Harness.WithAdmin("admin-1");

        await harness.Emergency.SetLocalPasswordAsync(new UserId("admin-1"), "quiet-maple-lantern-harbor", Cli);

        var operation = harness.Recorder.Operations.Should().ContainSingle().Which;
        operation.Operation.Actor.Should().Be(AuditActor.EmergencyCli);
        var row = operation.Staged.Should().ContainSingle().Which;
        row.Action.Should().Be(AuditActionNames.PasswordSet);
        row.Fields.Values.Should().NotContain("quiet-maple-lantern-harbor");
    }

    [Fact]
    public async Task Posture_is_unhealthy_until_mfa_and_recovery_codes_exist()
    {
        var harness = Harness.WithAdmin("admin-1");
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);

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

        var userId = await harness.Emergency.ProvisionAsync("quiet-maple-lantern-harbor", Cli);

        var user = harness.Users.Get(userId.Value)!;
        user.IsBreakGlassEligible.Should().BeTrue();
        user.DisplayName.Should().Be(EmergencyAccessIdentity.DisplayName);
        user.Username.Should().Be(UsernamePolicy.RecoveryAccessUsername);
        user.IdentityLinks.Should().ContainSingle(l =>
            l.Source == IdentitySource.Local && l.ExternalId == EmergencyAccessIdentity.LocalUsername);
        user.AssignedRoleIds.Should().Contain(AdminRole);
        harness.Credentials.Disabled[userId.Value].Should().BeFalse();

        // The account, its credential and the row commit in one unit; the password is never recorded.
        var operation = harness.Recorder.Operations.Should().ContainSingle().Which;
        operation.Operation.Actor.Should().Be(AuditActor.EmergencyCli);
        var row = operation.Staged.Should().ContainSingle().Which;
        row.Action.Should().Be(AuditActionNames.RecoveryAccessProvisioned);
        row.Target!.Id.Should().Be(userId.Value);
        row.Fields["username"].Should().Be(UsernamePolicy.RecoveryAccessUsername);
        row.Fields.Values.Should().NotContain("quiet-maple-lantern-harbor");
    }

    [Fact]
    public async Task Provision_rejects_a_weak_password_without_creating_the_account()
    {
        var harness = Harness.Empty();
        harness.Roles.RoleExists = true;

        var act = () => harness.Emergency.ProvisionAsync("password", Cli).AsTask();

        await act.Should().ThrowAsync<PasswordPolicyException>();
        harness.Users.All.Should().BeEmpty();
        harness.Credentials.Disabled.Should().BeEmpty();
    }

    [Fact]
    public async Task Provision_is_idempotent_when_recovery_access_already_exists()
    {
        var harness = Harness.Empty();
        harness.Roles.RoleExists = true;
        var first = await harness.Emergency.ProvisionAsync("quiet-maple-lantern-harbor", Cli);

        var second = await harness.Emergency.ProvisionAsync("another-password", Cli);

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

        await harness.Emergency.EnsureCanonicalIdentityAsync(AuditActor.EmergencyCli);

        var user = harness.Users.Get("emerg-1")!;
        user.Id.Value.Should().Be("emerg-1");
        user.IsBreakGlassEligible.Should().BeTrue();
        user.DisplayName.Should().Be(EmergencyAccessIdentity.DisplayName);
        EmergencyAccessIdentity.LocalUsernameOf(user).Should().Be(EmergencyAccessIdentity.LocalUsername);
        user.Username.Should().Be(EmergencyAccessIdentity.LocalUsername, "canonicalization is a rename through the username foundation");
        harness.Credentials.Disabled["emerg-1"].Should().BeFalse();
        harness.Mfa.Enrolled["emerg-1"].Should().BeTrue();
        harness.Mfa.UnusedCodes["emerg-1"].Should().Be(9);

        // AUD-1.1: one structured row, staged in the rename's unit of work and attributed to the caller.
        var operation = harness.Recorder.Operations.Should().ContainSingle().Which;
        operation.Operation.Actor.Should().Be(AuditActor.EmergencyCli);
        var staged = operation.Staged.Should().ContainSingle().Which;
        staged.Action.Should().Be(EmergencyAccessAuditActionNames.Canonicalized);
        staged.Target!.Id.Should().Be("emerg-1");
        staged.Fields["previousUsername"].Should().Be("oldglass");
        staged.Fields["newUsername"].Should().Be(EmergencyAccessIdentity.LocalUsername);
    }

    [Fact]
    public async Task EnsureCanonical_from_the_host_is_attributed_to_the_system_not_the_command_line()
    {
        var harness = Harness.WithAdmin("emerg-1");
        harness.Users.Seed(Admin("emerg-1", designated: true, username: "oldglass", displayName: "Emergency Administrator"));

        await harness.Emergency.EnsureCanonicalIdentityAsync(AuditActor.System());

        harness.Recorder.Operations.Should().ContainSingle().Which.Operation.Actor.RecordedValue.Should().Be("system");
        harness.Recorder.Staged.Should().ContainSingle(e => e.Action == EmergencyAccessAuditActionNames.Canonicalized);
    }

    [Fact]
    public async Task EnsureCanonical_on_an_already_canonical_account_declares_a_no_op_and_stages_nothing()
    {
        var harness = Harness.WithAdmin("emerg-1");
        harness.Users.Seed(Admin("emerg-1", designated: true, username: EmergencyAccessIdentity.LocalUsername, displayName: EmergencyAccessIdentity.DisplayName));

        await harness.Emergency.EnsureCanonicalIdentityAsync(AuditActor.System());

        var operation = harness.Recorder.Operations.Should().ContainSingle().Which;
        operation.NoOp.Should().BeTrue();
        operation.Staged.Should().BeEmpty();
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
            false)
        { Username = EmergencyAccessIdentity.LocalUsername });

        var act = () => harness.Emergency.ProvisionAsync("quiet-maple-lantern-harbor", Cli).AsTask();

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
            false)
        { Username = EmergencyAccessIdentity.LocalUsername });
        harness.Credentials.Seed("emerg-1", disabled: false);
        harness.Mfa.Enrolled["emerg-1"] = true;
        harness.Mfa.UnusedCodes["emerg-1"] = 4;

        var act = () => harness.Emergency.EnsureCanonicalIdentityAsync(AuditActor.EmergencyCli).AsTask();

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
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);

        var act = () => harness.Emergency.DesignateAsync(new UserId("admin-2"), Cli).AsTask();

        var ex = await act.Should().ThrowAsync<EmergencyAccessException>();
        ex.Which.Code.Should().Be(EmergencyAccessErrorCodes.UsernameConflict);
        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeTrue();
        harness.Users.Get("admin-2")!.IsBreakGlassEligible.Should().BeFalse();
    }

    [Fact]
    public async Task Designation_is_refused_when_another_user_owns_a_retired_breakglass_ledger_row()
    {
        // A former Recovery Access holder was renamed away from breakglass: no current username or Local
        // link exposes the name any more, but the ledger still binds it to them.
        var harness = Harness.WithAdmin("admin-1");
        harness.Users.Seed(Admin("former-ra", designated: false, username: "former.admin"));
        harness.Users.SeedRetiredLedgerEntry(EmergencyAccessIdentity.LocalUsername, "former-ra");

        var act = () => harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli).AsTask();

        var ex = await act.Should().ThrowAsync<EmergencyAccessException>();
        ex.Which.Code.Should().Be(EmergencyAccessErrorCodes.UsernameConflict);
        var candidate = harness.Users.Get("admin-1")!;
        candidate.IsBreakGlassEligible.Should().BeFalse("the refusal happens before any designation change");
        candidate.Username.Should().Be("admin-1");
        harness.Users.LedgerEntry(EmergencyAccessIdentity.LocalUsername)!.UserId.Value.Should().Be("former-ra");
        harness.Recorder.Staged.Should().BeEmpty();
    }

    [Fact]
    public async Task Provision_is_refused_when_another_user_owns_a_retired_breakglass_ledger_row()
    {
        var harness = Harness.Empty();
        harness.Roles.RoleExists = true;
        harness.Users.Seed(Admin("former-ra", designated: false, username: "former.admin"));
        harness.Users.SeedRetiredLedgerEntry(EmergencyAccessIdentity.LocalUsername, "former-ra");

        var act = () => harness.Emergency.ProvisionAsync("quiet-maple-lantern-harbor", Cli).AsTask();

        var ex = await act.Should().ThrowAsync<EmergencyAccessException>();
        ex.Which.Code.Should().Be(EmergencyAccessErrorCodes.UsernameConflict);
        harness.Users.AddAttempts.Should().Be(0, "the ledger pre-check refuses before any user is written");
        harness.Users.All.Should().NotContain(u => u.IsBreakGlassEligible);
        harness.Users.LedgerEntry(EmergencyAccessIdentity.LocalUsername)!.UserId.Value.Should().Be("former-ra");
        harness.Credentials.Disabled.Should().BeEmpty();
    }

    private static User Admin(string id, bool designated, string? username = null, string? displayName = null) => new(
        new UserId(id),
        displayName ?? "Platform Administrator",
        [new IdentityLink(IdentitySource.Local, username ?? id, Now)],
        [AdminRole],
        designated)
    { Username = username ?? id };

    private sealed class Harness
    {
        public RecordingUsers Users { get; } = new();
        public RecordingCredentials Credentials { get; } = new();
        public RecordingRoles Roles { get; } = new();
        public FakeMfa Mfa { get; } = new();
        public RecordingAuditedUnitOfWork Recorder { get; } = new();
        public EmergencyAccessService Emergency { get; }

        private Harness()
        {
            Emergency = new EmergencyAccessService(
                Users,
                Roles,
                Credentials,
                new LocalPasswordHasher(),
                Mfa,
                TimeProvider.System,
                NullLogger<EmergencyAccessService>.Instance,
                Recorder);
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
                false)
            { Username = "operator" });
            harness.Credentials.Seed("usr-1", disabled: false);
            return harness;
        }
    }

    /// <summary>
    /// In-memory users plus a username ledger kept separately from current usernames, so a retired name
    /// can be owned by a user who no longer holds it (and has no Local link exposing it).
    /// </summary>
    private sealed class RecordingUsers : IUserRepository
    {
        private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);
        private readonly Dictionary<string, UsernameLedgerEntry> _ledger = new(StringComparer.Ordinal);

        public int AddAttempts { get; private set; }

        /// <summary>Stores the user and makes its username its current ledger row (retiring any previous one).</summary>
        public void Seed(User user)
        {
            _users[user.Id.Value] = user;
            RetireCurrent(user.Id, except: user.Username);
            _ledger[user.Username] = new UsernameLedgerEntry(user.Username, user.Id, Now, null, null);
        }

        /// <summary>A retired ledger row owned by <paramref name="ownerId"/>, independent of any current username.</summary>
        public void SeedRetiredLedgerEntry(string username, string ownerId) =>
            _ledger[username] = new UsernameLedgerEntry(username, new UserId(ownerId), Now, Now, UsernameRetiredReasons.Rename);

        public UsernameLedgerEntry? LedgerEntry(string username) => _ledger.GetValueOrDefault(username);
        public User? Get(string id) => _users.GetValueOrDefault(id);
        public IReadOnlyCollection<User> All => _users.Values;

        private void RetireCurrent(UserId userId, string except)
        {
            foreach (var entry in _ledger.Values.Where(e => e.UserId == userId && e.IsCurrent && e.Username != except).ToList())
                _ledger[entry.Username] = entry with { RetiredAtUtc = Now, RetiredReason = UsernameRetiredReasons.Rename };
        }
        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.Count);
        public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.GetValueOrDefault(id.Value));
        public ValueTask<User?> FindDesignatedEmergencyUserAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u => u.IsBreakGlassEligible));
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u => u.IdentityLinks.Any(l => l.Source == source && l.ExternalId == externalId)));
        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default)
        {
            AddAttempts++;
            // As the ledger primary key: a name anyone holds or held is taken.
            if (_ledger.ContainsKey(user.Username))
                throw new UsernameException(UsernameErrorCodes.Taken, "The username is already taken.");
            Seed(user);
            return ValueTask.CompletedTask;
        }
        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default)
        {
            _users[user.Id.Value] = user;
            return ValueTask.CompletedTask;
        }
        public ValueTask<UsernameRenameResult> ChangeUsernameAsync(
            UserId userId,
            string expectedCurrentUsername,
            string newUsername,
            string retiredReason,
            string? newDisplayName = null,
            CancellationToken cancellationToken = default)
        {
            var user = _users[userId.Value];
            if (!string.Equals(user.Username, expectedCurrentUsername, StringComparison.Ordinal))
                throw new UsernameException(UsernameErrorCodes.ConcurrentChange, "The username changed concurrently.");
            // As the ledger primary key: a name owned, current or retired, by another user is taken.
            if (_ledger.TryGetValue(newUsername, out var owned) && owned.UserId != userId)
                throw new UsernameException(UsernameErrorCodes.Taken, "The username is already taken.");
            var reclaimed = owned is not null && !string.Equals(expectedCurrentUsername, newUsername, StringComparison.Ordinal);
            var links = user.IdentityLinks
                .Select(l =>
                    l.Source == IdentitySource.Local && l.ExternalId == expectedCurrentUsername
                        ? l with { ExternalId = newUsername }
                        : l)
                .ToList();
            Seed(user with
            {
                Username = newUsername,
                DisplayName = newDisplayName ?? user.DisplayName,
                IdentityLinks = links,
            });
            return ValueTask.FromResult(new UsernameRenameResult(
                string.Equals(expectedCurrentUsername, newUsername, StringComparison.Ordinal)
                    ? UsernameChangeOutcome.Unchanged
                    : reclaimed ? UsernameChangeOutcome.Reclaimed : UsernameChangeOutcome.Assigned,
                expectedCurrentUsername,
                newUsername));
        }
        public ValueTask<UsernameLedgerEntry?> FindUsernameLedgerEntryAsync(string username, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_ledger.GetValueOrDefault(username));
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
        public ValueTask<IReadOnlyList<string>> CompleteEnrollmentAsync(UserId userId, string totpCode, AuditActor actor, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public ValueTask<bool> VerifyTotpAsync(UserId userId, string totpCode, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<bool> ConsumeRecoveryCodeAsync(UserId userId, string recoveryCode, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(UserId userId, AuditActor actor, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public ValueTask ResetAsync(UserId userId, AuditActor actor, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<int> CountUnusedRecoveryCodesAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(UnusedCodes.GetValueOrDefault(userId.Value));
    }
}
