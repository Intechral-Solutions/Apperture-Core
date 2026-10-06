using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// Policy and orchestration of <see cref="UsernameService"/>. The ledger semantics here are a faithful
/// in-memory model; the real database invariants are proven by the SQL Server tests.
/// </summary>
public sealed class UsernameServiceTests
{
    private static User NewEntraUser(string id, string username) =>
        new(new UserId(id), "Someone", [new IdentityLink(IdentitySource.Entra, "oid-" + id, null, TenantId: "tenant")], [], IsBreakGlassEligible: false)
        {
            Username = username
        };

    private static User NewLocalUser(string id, string username, bool recoveryAccess = false) =>
        new(new UserId(id), "Someone", [new IdentityLink(IdentitySource.Local, username, null)], [], IsBreakGlassEligible: recoveryAccess)
        {
            Username = username
        };

    [Fact]
    public async Task Generation_skips_names_held_current_or_retired_by_anyone()
    {
        var repo = new LedgerUserRepository();
        await repo.AddAsync(NewEntraUser("u1", "jane.doe"));
        await repo.AddAsync(NewEntraUser("u2", "jane.doe-2"));
        repo.Retire("jane.doe-2"); // a retired name is never reassigned to another user
        await repo.AddAsync(NewEntraUser("u3", "jane.doe-3"));
        await repo.ChangeUsernameAsync(new UserId("u3"), "jane.doe-3", "jdoe", UsernameRetiredReasons.Rename);

        var created = await new UsernameService(repo).AddWithGeneratedUsernameAsync(
            name => NewEntraUser("new", name),
            new UsernameGenerationSources("Jane.Doe@contoso.com", null, null));

        created.Username.Should().Be("jane.doe-4");
        repo.Ledger["jane.doe-3"].UserId.Should().Be("u3", "the retired name stays bound to its first owner");
    }

    [Fact]
    public async Task Generation_moves_on_when_the_database_reports_a_lost_race()
    {
        var repo = new LedgerUserRepository { RaceOnNextAdd = "alice" };

        var created = await new UsernameService(repo).AddWithGeneratedUsernameAsync(
            name => NewEntraUser("new", name),
            new UsernameGenerationSources(null, "alice@contoso.com", null));

        created.Username.Should().Be("alice-2", "the pre-check said free, but the ledger insert is the arbiter");
    }

    [Fact]
    public async Task Generation_exhaustion_fails_and_reserves_nothing()
    {
        var repo = new LedgerUserRepository { RejectEveryAdd = true };

        var act = async () => await new UsernameService(repo, () => "zzzzzz").AddWithGeneratedUsernameAsync(
            name => NewEntraUser("new", name),
            new UsernameGenerationSources(null, null, "Alice"));

        (await act.Should().ThrowAsync<UsernameException>()).Which.Code.Should().Be(UsernameErrorCodes.GenerationExhausted);
        repo.AddAttempts.Should().Be(25, "base, -2 to -20, then five random suffixes");
        repo.Ledger.Should().BeEmpty();
    }

    [Fact]
    public async Task Rename_assigns_a_new_name_and_keeps_the_old_one_bound_to_the_same_user()
    {
        var repo = new LedgerUserRepository();
        await repo.AddAsync(NewLocalUser("u1", "jane@contoso.com")); // grandfathered

        var outcome = await new UsernameService(repo).RenameAsync(new UserId("u1"), "  Jane.Doe ");

        outcome.Should().Be(UsernameChangeOutcome.Assigned);
        var user = (await repo.GetByIdAsync(new UserId("u1")))!;
        user.Username.Should().Be("jane.doe");
        user.IdentityLinks.Single(l => l.Source == IdentitySource.Local).ExternalId.Should().Be("jane.doe");
        repo.Ledger["jane@contoso.com"].Should().Match<LedgerUserRepository.Row>(r =>
            r.UserId == "u1" && r.RetiredAtUtc != null && r.RetiredReason == UsernameRetiredReasons.Rename);
    }

    [Fact]
    public async Task Same_user_may_reclaim_an_own_retired_name_that_satisfies_the_policy()
    {
        var repo = new LedgerUserRepository();
        await repo.AddAsync(NewLocalUser("u1", "alice"));
        await new UsernameService(repo).RenameAsync(new UserId("u1"), "alice.smith");

        var outcome = await new UsernameService(repo).RenameAsync(new UserId("u1"), "alice");

        outcome.Should().Be(UsernameChangeOutcome.Reclaimed);
        repo.Ledger["alice"].Should().Match<LedgerUserRepository.Row>(r => r.UserId == "u1" && r.RetiredAtUtc == null && r.RetiredReason == null);
        repo.Ledger["alice.smith"].RetiredAtUtc.Should().NotBeNull();
        repo.Ledger.Values.Count(r => r.UserId == "u1" && r.RetiredAtUtc == null).Should().Be(1);
    }

    [Fact]
    public async Task Another_users_current_or_retired_name_is_taken()
    {
        var repo = new LedgerUserRepository();
        await repo.AddAsync(NewLocalUser("u1", "alice"));
        await repo.AddAsync(NewLocalUser("u2", "bob"));
        await new UsernameService(repo).RenameAsync(new UserId("u1"), "alice.smith"); // "alice" now retired, owned by u1
        var service = new UsernameService(repo);

        var current = async () => await service.RenameAsync(new UserId("u2"), "alice.smith");
        var retired = async () => await service.RenameAsync(new UserId("u2"), "alice");

        (await current.Should().ThrowAsync<UsernameException>()).Which.Code.Should().Be(UsernameErrorCodes.Taken);
        (await retired.Should().ThrowAsync<UsernameException>()).Which.Code.Should().Be(UsernameErrorCodes.Taken);
        repo.Ledger["alice"].UserId.Should().Be("u1");
        (await repo.GetByIdAsync(new UserId("u2")))!.Username.Should().Be("bob");
    }

    [Theory]
    [InlineData("system", UsernameErrorCodes.Reserved)]
    [InlineData("breakglass", UsernameErrorCodes.Reserved)]
    [InlineData("jane@contoso.com", UsernameErrorCodes.Invalid)]
    public async Task Ordinary_rename_and_reclaim_obey_the_current_policy(string target, string code)
    {
        var repo = new LedgerUserRepository();
        await repo.AddAsync(NewLocalUser("u1", "jane.doe"));
        // u1 once held the grandfathered name; it still may not be reclaimed because it fails the policy.
        repo.SeedRetired("jane@contoso.com", "u1");

        var act = async () => await new UsernameService(repo).RenameAsync(new UserId("u1"), target);

        (await act.Should().ThrowAsync<UsernameException>()).Which.Code.Should().Be(code);
        repo.ChangeCalls.Should().Be(0);
    }

    [Fact]
    public async Task Ordinary_rename_refuses_recovery_access()
    {
        var repo = new LedgerUserRepository();
        await repo.AddAsync(NewLocalUser("ra", "breakglass", recoveryAccess: true));

        var act = async () => await new UsernameService(repo).RenameAsync(new UserId("ra"), "someone");

        (await act.Should().ThrowAsync<EmergencyAccessException>()).Which.Code.Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);
        repo.ChangeCalls.Should().Be(0);
    }

    [Fact]
    public async Task Rename_to_the_current_name_is_a_no_op()
    {
        var repo = new LedgerUserRepository();
        await repo.AddAsync(NewLocalUser("u1", "alice"));

        var outcome = await new UsernameService(repo).RenameAsync(new UserId("u1"), " ALICE ");

        outcome.Should().Be(UsernameChangeOutcome.Unchanged);
        repo.ChangeCalls.Should().Be(0);
    }

    [Fact]
    public async Task Recovery_access_canonicalization_assigns_breakglass_and_retires_the_personal_name()
    {
        var repo = new LedgerUserRepository();
        await repo.AddAsync(NewLocalUser("u1", "pat.admin", recoveryAccess: true));

        var outcome = await new UsernameService(repo).ApplyRecoveryAccessIdentityAsync((await repo.GetByIdAsync(new UserId("u1")))!);

        outcome.Should().Be(UsernameChangeOutcome.Assigned);
        var user = (await repo.GetByIdAsync(new UserId("u1")))!;
        user.Username.Should().Be("breakglass");
        user.DisplayName.Should().Be(EmergencyAccessIdentity.DisplayName);
        repo.Ledger["pat.admin"].Should().Match<LedgerUserRepository.Row>(r =>
            r.UserId == "u1" && r.RetiredReason == UsernameRetiredReasons.Canonicalization);
    }

    [Fact]
    public async Task Only_the_designated_user_can_take_the_recovery_access_name()
    {
        var repo = new LedgerUserRepository();
        await repo.AddAsync(NewLocalUser("u1", "pat.admin"));

        var act = async () => await new UsernameService(repo).ApplyRecoveryAccessIdentityAsync((await repo.GetByIdAsync(new UserId("u1")))!);

        await act.Should().ThrowAsync<InvalidOperationException>();
        repo.ChangeCalls.Should().Be(0);
    }

    /// <summary>In-memory model of the username ledger: primary key on the name, owner never changes.</summary>
    internal sealed class LedgerUserRepository : IUserRepository
    {
        internal sealed record Row(string UserId, DateTimeOffset? RetiredAtUtc, string? RetiredReason);

        private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);

        public Dictionary<string, Row> Ledger { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? RaceOnNextAdd { get; set; }
        public bool RejectEveryAdd { get; set; }
        public int AddAttempts { get; private set; }
        public int ChangeCalls { get; private set; }

        public void Retire(string username) =>
            Ledger[username] = Ledger[username] with { RetiredAtUtc = DateTimeOffset.UtcNow, RetiredReason = UsernameRetiredReasons.Rename };

        public void SeedRetired(string username, string userId) =>
            Ledger[username] = new Row(userId, DateTimeOffset.UtcNow, UsernameRetiredReasons.Rename);

        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default)
        {
            AddAttempts++;
            if (RejectEveryAdd || Ledger.ContainsKey(user.Username))
                throw new UsernameException(UsernameErrorCodes.Taken, "taken");
            if (RaceOnNextAdd == user.Username)
            {
                RaceOnNextAdd = null;
                Ledger[user.Username] = new Row("racer", null, null);
                throw new UsernameException(UsernameErrorCodes.Taken, "taken");
            }

            Ledger[user.Username] = new Row(user.Id.Value, null, null);
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
            ChangeCalls++;
            var user = _users[userId.Value];
            if (user.Username != expectedCurrentUsername)
                throw new UsernameException(UsernameErrorCodes.ConcurrentChange, "changed");

            var previousUsername = user.Username;
            var outcome = UsernameChangeOutcome.Unchanged;
            if (user.Username != newUsername)
            {
                if (Ledger.TryGetValue(newUsername, out var existing) && existing.UserId != userId.Value)
                    throw new UsernameException(UsernameErrorCodes.Taken, "taken");

                Ledger[user.Username] = Ledger[user.Username] with { RetiredAtUtc = DateTimeOffset.UtcNow, RetiredReason = retiredReason };
                outcome = existing is null ? UsernameChangeOutcome.Assigned : UsernameChangeOutcome.Reclaimed;
                Ledger[newUsername] = new Row(userId.Value, null, null);
                user = user with
                {
                    Username = newUsername,
                    IdentityLinks = user.IdentityLinks
                        .Select(l => l.Source == IdentitySource.Local ? l with { ExternalId = newUsername } : l)
                        .ToList()
                };
            }

            _users[userId.Value] = user with { DisplayName = newDisplayName ?? user.DisplayName };
            return ValueTask.FromResult(new UsernameRenameResult(outcome, previousUsername, user.Username));
        }

        public ValueTask<UsernameLedgerEntry?> FindUsernameLedgerEntryAsync(string username, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Ledger.TryGetValue(username, out var row)
                ? new UsernameLedgerEntry(username, new UserId(row.UserId), DateTimeOffset.UtcNow, row.RetiredAtUtc, row.RetiredReason)
                : null);

        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.Count);
        public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.GetValueOrDefault(id.Value));
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u => u.IdentityLinks.Any(l => l.Source == source && l.ExternalId == externalId)));
        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default)
        {
            _users[user.Id.Value] = user;
            return ValueTask.CompletedTask;
        }
        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<(IReadOnlyList<User>, int)>((_users.Values.ToList(), _users.Count));
        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
