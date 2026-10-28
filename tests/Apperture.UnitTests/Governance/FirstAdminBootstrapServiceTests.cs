using Apperture.Core.Application.Services;
using Apperture.UnitTests.AdministratorViability;
using Apperture.Core.Domain.Identity;
using Apperture.ControlPlane.Security;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class FirstAdminBootstrapServiceTests
{
    private static Apperture.ControlPlane.Security.AdministratorViability.IAdministratorViabilityCoordinator NewViability() => FakeViability.Inert();

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, true, false)]
    [InlineData(3, true, false)]
    public async Task GetSetupStatusAsync_reflects_user_count_gate(int userCount, bool initialized, bool allowsFirstAdmin)
    {
        var service = new FirstAdminBootstrapService(
            new CountingUserRepository(userCount),
            new StubRoleRepository());

        var status = await service.GetSetupStatusAsync();

        status.Initialized.Should().Be(initialized);
        status.AllowsFirstAdmin.Should().Be(allowsFirstAdmin);
    }

    [Theory]
    [InlineData("breakglass")]
    [InlineData("BreakGlass")]
    [InlineData("  BREAKGLASS  ")]
    public async Task TryCreateFirstAdminAsync_rejects_the_reserved_recovery_username(string username)
    {
        var users = new CapturingUserRepository();
        var service = new FirstAdminBootstrapService(users, new PresentRoleRepository(), auditedUnitOfWork: new Apperture.UnitTests.Audit.AuditRecording(), viability: NewViability());

        var result = await service.TryCreateFirstAdminAsync("First admin", IdentitySource.Local, username, "unique-local-passphrase");

        result.Kind.Should().Be(FirstAdminBootstrapResultKind.DuplicateIdentityLink);
        users.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task TryCreateFirstAdminAsync_assigns_the_normalized_sign_in_name_as_username_and_local_link()
    {
        var users = new CapturingUserRepository();
        var service = new FirstAdminBootstrapService(users, new PresentRoleRepository(), auditedUnitOfWork: new Apperture.UnitTests.Audit.AuditRecording(), viability: NewViability());

        var result = await service.TryCreateFirstAdminAsync("First admin", IdentitySource.Local, "  Admin  ", "unique-local-passphrase");

        result.Kind.Should().Be(FirstAdminBootstrapResultKind.Success);
        var user = users.Added.Should().ContainSingle().Subject;
        user.Username.Should().Be("admin", "admin is not reserved and is the natural first-admin name");
        user.IdentityLinks.Should().ContainSingle(l => l.Source == IdentitySource.Local)
            .Which.ExternalId.Should().Be(user.Username);
    }

    [Fact]
    public async Task The_first_administrator_commits_with_an_anonymous_created_row_and_a_refusal_writes_none()
    {
        var users = new CapturingUserRepository();
        var audit = new Apperture.UnitTests.Audit.AuditRecording();
        var service = new FirstAdminBootstrapService(users, new PresentRoleRepository(), auditedUnitOfWork: audit, viability: NewViability());

        var result = await service.TryCreateFirstAdminAsync("First admin", IdentitySource.Local, "admin", password: null);
        var reserved = await service.TryCreateFirstAdminAsync("First admin", IdentitySource.Local, "breakglass", password: null);

        reserved.Kind.Should().Be(FirstAdminBootstrapResultKind.DuplicateIdentityLink);
        var (created, actor) = audit.Events.Should().ContainSingle().Subject;
        created.Action.Should().Be(Apperture.Core.Application.Audit.AuditActionNames.FirstAdminCreated);
        actor.Should().Be(Apperture.Core.Application.Audit.AuditActor.Anonymous, "the bootstrap request is unauthenticated");
        created.Target.Should().Be(new Apperture.Core.Application.Audit.AuditTarget(Apperture.Core.Application.Audit.AuditTargetTypes.User, result.UserId!.Value));
        created.Fields["localSignInReady"].Should().Be(false);
        audit.Unit.Operations.Should().HaveCount(2).And.Contain(o => o.NoOp, "the refused attempt changed nothing");
    }

    [Fact]
    public async Task An_installation_that_is_already_set_up_answers_without_entering_the_audited_unit_or_reading_anything_else()
    {
        var users = new SequencedCountUserRepository(1);
        var audit = new Apperture.UnitTests.Audit.AuditRecording();
        var service = new FirstAdminBootstrapService(users, new PresentRoleRepository(), auditedUnitOfWork: audit, viability: NewViability());

        var result = await service.TryCreateFirstAdminAsync("First admin", IdentitySource.Local, "admin", "unique-local-passphrase");

        result.Kind.Should().Be(FirstAdminBootstrapResultKind.AlreadyExists);
        audit.Unit.Operations.Should().BeEmpty("an anonymous request for a set-up installation must not queue behind a unit");
        users.CountReads.Should().Be(1, "one inexpensive read, and nothing else");
        users.Added.Should().BeEmpty();
        audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task The_early_count_never_allows_a_creation_it_is_decided_again_where_the_user_is_written()
    {
        // The installation is empty when the early read is made and has an administrator by the time the unit looks.
        var users = new SequencedCountUserRepository(0, 1);
        var audit = new Apperture.UnitTests.Audit.AuditRecording();
        var service = new FirstAdminBootstrapService(users, new PresentRoleRepository(), auditedUnitOfWork: audit, viability: NewViability());

        var result = await service.TryCreateFirstAdminAsync("First admin", IdentitySource.Local, "admin", "unique-local-passphrase");

        result.Kind.Should().Be(FirstAdminBootstrapResultKind.AlreadyExists);
        users.Added.Should().BeEmpty("the early read of an empty installation is not authority to create");
        audit.Unit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
        users.CountReads.Should().Be(2);
    }

    [Fact]
    public async Task The_gate_is_taken_after_the_early_answer_and_before_the_decisive_count_read()
    {
        var gate = new RecordingGate();
        var gateHeldAtEachCountRead = new List<int>();
        var users = new SequencedCountUserRepository(0) { OnCountRead = _ => gateHeldAtEachCountRead.Add(gate.Acquired) };
        var service = new FirstAdminBootstrapService(
            users, new PresentRoleRepository(), auditedUnitOfWork: new Apperture.UnitTests.Audit.AuditRecording(), viability: FakeViability.Scripted(gate));

        var result = await service.TryCreateFirstAdminAsync("First admin", IdentitySource.Local, "admin", "unique-local-passphrase");

        result.Kind.Should().Be(FirstAdminBootstrapResultKind.Success);
        gateHeldAtEachCountRead.Should().Equal([0, 1], "the early answer takes no lock; the read that decides is made with the gate held");
        gate.Acquired.Should().Be(1);
    }

    [Fact]
    public async Task An_installation_that_is_already_set_up_never_touches_the_gate()
    {
        var gate = new RecordingGate();
        var users = new SequencedCountUserRepository(1);
        var service = new FirstAdminBootstrapService(
            users, new PresentRoleRepository(), auditedUnitOfWork: new Apperture.UnitTests.Audit.AuditRecording(), viability: FakeViability.Scripted(gate));

        var result = await service.TryCreateFirstAdminAsync("First admin", IdentitySource.Local, "admin", "unique-local-passphrase");

        result.Kind.Should().Be(FirstAdminBootstrapResultKind.AlreadyExists);
        gate.Acquired.Should().Be(0, "an anonymous caller must not queue on the global gate once the installation is set up");
    }

    [Fact]
    public async Task A_refusal_decided_under_the_gate_still_declares_the_attempt_a_no_op()
    {
        var gate = new RecordingGate();
        var users = new SequencedCountUserRepository(0, 1);
        var audit = new Apperture.UnitTests.Audit.AuditRecording();
        var service = new FirstAdminBootstrapService(users, new PresentRoleRepository(), auditedUnitOfWork: audit, viability: FakeViability.Scripted(gate));

        var result = await service.TryCreateFirstAdminAsync("First admin", IdentitySource.Local, "admin", "unique-local-passphrase");

        result.Kind.Should().Be(FirstAdminBootstrapResultKind.AlreadyExists, "the competing request committed first");
        gate.Acquired.Should().Be(1);
        audit.Unit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
        users.Added.Should().BeEmpty();
        audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task A_busy_gate_is_contention_nothing_is_read_or_written_in_the_unit_and_no_row_is_recorded()
    {
        var busy = new Apperture.Core.Application.AdministratorViability.PlatformAdministratorSetBusyException(
            Apperture.Core.Application.AdministratorViability.PlatformAdministratorSetLockOutcome.TimedOut);
        var gate = new RecordingGate { Fail = busy };
        var users = new SequencedCountUserRepository(0);
        var audit = new Apperture.UnitTests.Audit.AuditRecording();
        var service = new FirstAdminBootstrapService(users, new PresentRoleRepository(), auditedUnitOfWork: audit, viability: FakeViability.Scripted(gate));

        var act = async () => await service.TryCreateFirstAdminAsync("First admin", IdentitySource.Local, "admin", "unique-local-passphrase");

        (await act.Should().ThrowAsync<Apperture.Core.Application.AdministratorViability.PlatformAdministratorSetBusyException>()).Which.Should().BeSameAs(busy);
        users.CountReads.Should().Be(1, "only the early answer was read: the decisive read waits for the gate");
        users.Added.Should().BeEmpty();
        audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task A_gate_that_failed_for_a_reason_other_than_contention_is_not_hidden_and_a_missing_coordinator_fails_closed()
    {
        var failing = new RecordingGate { Fail = new Apperture.Core.Application.AdministratorViability.PlatformAdministratorSetGateException(-999) };
        var users = new SequencedCountUserRepository(0);
        var withFailingGate = new FirstAdminBootstrapService(
            users, new PresentRoleRepository(), auditedUnitOfWork: new Apperture.UnitTests.Audit.AuditRecording(), viability: FakeViability.Scripted(failing));
        var withoutCoordinator = new FirstAdminBootstrapService(
            users, new PresentRoleRepository(), auditedUnitOfWork: new Apperture.UnitTests.Audit.AuditRecording());

        var failed = async () => await withFailingGate.TryCreateFirstAdminAsync("First admin", IdentitySource.Local, "admin", "unique-local-passphrase");
        var unguarded = async () => await withoutCoordinator.TryCreateFirstAdminAsync("First admin", IdentitySource.Local, "admin", "unique-local-passphrase");

        await failed.Should().ThrowAsync<Apperture.Core.Application.AdministratorViability.PlatformAdministratorSetGateException>();
        await unguarded.Should().ThrowAsync<InvalidOperationException>("an unserialised first administrator is the defect this closes");
        users.Added.Should().BeEmpty();
    }

    [Theory]
    [InlineData("jane@contoso.com", UsernameErrorCodes.Invalid)]
    [InlineData("ab", UsernameErrorCodes.Invalid)]
    [InlineData("system", UsernameErrorCodes.Reserved)]
    public async Task TryCreateFirstAdminAsync_refuses_a_name_that_fails_the_username_policy(string username, string code)
    {
        var users = new CapturingUserRepository();
        var service = new FirstAdminBootstrapService(users, new PresentRoleRepository(), auditedUnitOfWork: new Apperture.UnitTests.Audit.AuditRecording(), viability: NewViability());

        var act = async () => await service.TryCreateFirstAdminAsync("First admin", IdentitySource.Local, username, "unique-local-passphrase");

        (await act.Should().ThrowAsync<UsernameException>()).Which.Code.Should().Be(code);
        users.Added.Should().BeEmpty();
    }

    private sealed class CapturingUserRepository : IUserRepository
    {
        public List<User> Added { get; } = [];

        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask<User?> GetByIdAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<User?>(null);
        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) => ValueTask.FromResult<(IReadOnlyList<User>, int)>(([], 0));
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.FromResult<User?>(null);
        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default)
        {
            Added.Add(user);
            return ValueTask.CompletedTask;
        }
        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    /// <summary>Reports the given user counts in turn (the last one repeats), and records every read and every user added.</summary>
    private sealed class SequencedCountUserRepository(params int[] counts) : IUserRepository
    {
        public List<User> Added { get; } = [];
        public int CountReads { get; private set; }

        /// <summary>Called with the number of the read (from 1) each time the count is read.</summary>
        public Action<int>? OnCountRead { get; init; }

        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default)
        {
            var count = counts[Math.Min(CountReads++, counts.Length - 1)];
            OnCountRead?.Invoke(CountReads);
            return ValueTask.FromResult(count);
        }
        public ValueTask<User?> GetByIdAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<User?>(null);
        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) => ValueTask.FromResult<(IReadOnlyList<User>, int)>(([], 0));
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.FromResult<User?>(null);
        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default)
        {
            Added.Add(user);
            return ValueTask.CompletedTask;
        }
        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class PresentRoleRepository : IRoleRepository
    {
        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Role?>(new Role(roleId, "Platform Administrator", null, [], true));
        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class CountingUserRepository : IUserRepository
    {
        private readonly int _count;

        public CountingUserRepository(int count) => _count = count;

        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_count);
        public ValueTask<User?> GetByIdAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<User?>(null);
        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) => ValueTask.FromResult<(IReadOnlyList<User>, int)>(([], 0));
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.FromResult<User?>(null);
        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class StubRoleRepository : IRoleRepository
    {
        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult<Role?>(null);
        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
