using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.UnitTests.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class UserManagementServiceStrongAuthenticationTests
{
    [Fact]
    public async Task Reenable_without_recent_strong_authentication_is_rejected()
    {
        var creds = new RecordingCredentials("usr-1", disabled: true);
        var audit = new AuditRecording();
        var service = Create(creds, new RejectingGate(StrongAuthenticationErrorCodes.Required), audit: audit);

        var act = () => service.SetLocalCredentialDisabledAsync(new UserId("usr-1"), disabled: false).AsTask();

        var ex = await act.Should().ThrowAsync<StrongAuthenticationException>();
        ex.Which.Code.Should().Be(StrongAuthenticationErrorCodes.Required);
        creds.Disabled.Should().BeTrue();
        creds.Hash.Should().Be("hash-original");

        // AUD-1.2: the refusal is decided inside the transaction and recorded after it rolled back, as a W3 row.
        audit.Actions.Should().Equal(StrongAuthenticationAuditActionNames.AssuranceInsufficient);
        audit.Writer.Written.Single().BestEffort.Should().BeTrue();
        audit.Last.Fields["code"].Should().Be(StrongAuthenticationErrorCodes.Required);
        audit.Last.Target!.Id.Should().Be("usr-1");
    }

    [Fact]
    public async Task A_failed_refusal_row_does_not_change_the_refusal_the_caller_sees()
    {
        var creds = new RecordingCredentials("usr-1", disabled: true);
        var audit = new AuditRecording { FailWrites = true };
        var service = Create(creds, new RejectingGate(StrongAuthenticationErrorCodes.Required), audit: audit);

        var act = () => service.SetLocalCredentialDisabledAsync(new UserId("usr-1"), disabled: false).AsTask();

        (await act.Should().ThrowAsync<StrongAuthenticationException>()).Which.Code.Should().Be(StrongAuthenticationErrorCodes.Required);
        audit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task Reenabling_an_enabled_credential_changes_nothing_and_records_nothing()
    {
        var creds = new RecordingCredentials("usr-1", disabled: false);
        var audit = new AuditRecording();
        var service = Create(creds, new AllowingGate(), audit: audit);

        await service.SetLocalCredentialDisabledAsync(new UserId("usr-1"), disabled: false);

        creds.Disabled.Should().BeFalse();
        audit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task Permission_alone_does_not_reenable_local_authentication()
    {
        var creds = new RecordingCredentials("usr-1", disabled: true);
        var service = Create(creds, new RejectingGate(StrongAuthenticationErrorCodes.Required));

        await Assert.ThrowsAsync<StrongAuthenticationException>(() =>
            service.SetLocalCredentialDisabledAsync(new UserId("usr-1"), disabled: false).AsTask());
    }

    [Fact]
    public async Task Reenable_with_recent_strong_authentication_succeeds_and_keeps_the_hash()
    {
        var creds = new RecordingCredentials("usr-1", disabled: true);
        var audit = new AuditRecording();
        var service = Create(creds, new AllowingGate(), audit: audit);

        await service.SetLocalCredentialDisabledAsync(new UserId("usr-1"), disabled: false);

        creds.Disabled.Should().BeFalse();
        creds.Hash.Should().Be("hash-original");
        audit.Actions.Should().Equal(StrongAuthenticationAuditActionNames.LocalReenabledStrongAdmin);
        audit.Last.Target!.Id.Should().Be("usr-1");
    }

    [Fact]
    public async Task Reenable_fails_when_strong_authentication_is_not_configured()
    {
        var creds = new RecordingCredentials("usr-1", disabled: true);
        var service = Create(creds, new RejectingGate(StrongAuthenticationErrorCodes.NotConfigured));

        var ex = await Assert.ThrowsAsync<StrongAuthenticationException>(() =>
            service.SetLocalCredentialDisabledAsync(new UserId("usr-1"), disabled: false).AsTask());
        ex.Code.Should().Be(StrongAuthenticationErrorCodes.NotConfigured);
        creds.Disabled.Should().BeTrue();
    }

    [Fact]
    public async Task Ordinary_user_without_credential_manage_cannot_reenable_local_authentication()
    {
        var creds = new RecordingCredentials("usr-1", disabled: true);
        var service = Create(creds, new AllowingGate(), allowCredentialManage: false);

        var act = () => service.SetLocalCredentialDisabledAsync(new UserId("usr-1"), disabled: false).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        creds.Disabled.Should().BeTrue();
        creds.Hash.Should().Be("hash-original");
    }

    [Fact]
    public async Task Expired_assurance_does_not_reenable_local_authentication()
    {
        var creds = new RecordingCredentials("usr-1", disabled: true);
        var service = Create(creds, new RejectingGate(StrongAuthenticationErrorCodes.Expired));

        var ex = await Assert.ThrowsAsync<StrongAuthenticationException>(() =>
            service.SetLocalCredentialDisabledAsync(new UserId("usr-1"), disabled: false).AsTask());
        ex.Code.Should().Be(StrongAuthenticationErrorCodes.Expired);
        creds.Disabled.Should().BeTrue();
        creds.Hash.Should().Be("hash-original");
    }

    private static UserManagementService Create(
        RecordingCredentials creds,
        IStrongAuthenticationGate gate,
        bool allowCredentialManage = true,
        AuditRecording? audit = null)
    {
        audit ??= new AuditRecording();
        var user = new User(
            new UserId("usr-1"),
            "Operator",
            [new IdentityLink(IdentitySource.Local, "operator", DateTimeOffset.UtcNow)],
            [],
            false)
        { Username = "operator" };
        return new UserManagementService(
            new StubPrincipal(),
            new StubResolver(),
            allowCredentialManage ? new AllowAllAuth() : new DenyAuth(),
            new SingleUserRepository(user),
            new EmptyRoles(),
            new InMemoryResourceDescriptorRegistry(),
            creds,
            new StubHasher(),
            Options.Create(new LocalAuthOptions()),
            strongAuthentication: gate,
            auditedUnitOfWork: audit,
            auditEvents: audit);
    }

    private sealed class AllowingGate : IStrongAuthenticationGate
    {
        public ValueTask<AuthenticationAssurance> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuthenticationAssurance("entra-authentication-context", true, DateTimeOffset.UtcNow, "c1", true));

        public ValueTask RequireRecentStrongAuthenticationAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RejectingGate : IStrongAuthenticationGate
    {
        private readonly string _code;
        public RejectingGate(string code) => _code = code;

        public ValueTask<AuthenticationAssurance> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AuthenticationAssurance.None);

        public ValueTask RequireRecentStrongAuthenticationAsync(CancellationToken cancellationToken = default) =>
            throw new StrongAuthenticationException(_code, "rejected");
    }

    private sealed class RecordingCredentials : ILocalCredentialRepository
    {
        public RecordingCredentials(string userId, bool disabled)
        {
            UserId = userId;
            Disabled = disabled;
            Hash = "hash-original";
        }

        public string UserId { get; }
        public bool Disabled { get; private set; }
        public string Hash { get; }

        public ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<LocalCredentialData?>(new LocalCredentialData(userId, Hash, "salt", "stub", Disabled));

        public ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("IAM-2 re-enable must not replace the password.");

        public ValueTask SetDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default)
        {
            Disabled = disabled;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SingleUserRepository : IUserRepository
    {
        private readonly User _user;
        public SingleUserRepository(User user) => _user = user;
        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(1);
        public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<User?>(id.Value == _user.Id.Value ? _user : null);
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<User?>(_user);
        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<(IReadOnlyList<User>, int)>(([_user], 1));
        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CompleteEntraLinkProofAsync(string tenantId, string objectId, DateTimeOffset provenAt, string? registrationIdWhenMissing, string? providerSubject, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
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

    private sealed class AllowAllAuth : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(IActor actor, PermissionDescriptor permission, ResourceDescriptor? resource, IGovernedResourceIdentity? resourceIdentity = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class DenyAuth : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(IActor actor, PermissionDescriptor permission, ResourceDescriptor? resource, IGovernedResourceIdentity? resourceIdentity = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }

    private sealed class EmptyRoles : IRoleRepository
    {
        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult<Role?>(null);
        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class StubHasher : ILocalPasswordHasher
    {
        public string AlgorithmName => "stub";
        public (string Hash, string Salt) HashPassword(string password) => ("h", "s");
        public bool VerifyPassword(string password, string hash, string salt) => true;
    }
}
