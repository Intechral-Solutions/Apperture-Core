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

public sealed class UserManagementServiceIdentityLinkTests
{
    [Fact]
    public async Task AddIdentityLinkAsync_when_link_already_on_same_user_is_idempotent()
    {
        var userId = new UserId("usr-same-link");
        var user = new User(
            userId,
            "Operator",
            [new IdentityLink(IdentitySource.Entra, "entra-oid-same", null)],
            [],
            false);
        var repo = new InMemoryUserRepository(user);
        var service = NewService(repo);

        await service.AddIdentityLinkAsync(userId, IdentitySource.Entra, "entra-oid-same", CancellationToken.None);

        repo.IdentityLinkAddCount.Should().Be(0);
    }

    [Fact]
    public async Task AddIdentityLinkAsync_when_external_id_used_by_another_user_throws()
    {
        var targetId = new UserId("usr-target");
        var repo = new InMemoryUserRepository(
            new User(targetId, "Target", [new IdentityLink(IdentitySource.Local, "target-local", null)], [], false),
            new User(new UserId("usr-other"), "Other", [new IdentityLink(IdentitySource.Entra, "entra-oid-taken", null)], [], false));
        var service = NewService(repo);

        Func<Task> act = async () =>
            await service.AddIdentityLinkAsync(targetId, IdentitySource.Entra, "entra-oid-taken", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<IdentityLinkException>();
        ex.Which.Code.Should().Be(IdentityLinkErrorCodes.ExternalIdInUse);
    }

    [Fact]
    public async Task AddIdentityLinkAsync_rejects_a_second_entra_identity_on_the_same_user()
    {
        var userId = new UserId("usr-two-entra");
        var user = new User(
            userId,
            "Operator",
            [new IdentityLink(IdentitySource.Entra, FirstOid, DateTimeOffset.UtcNow)],
            [],
            false);
        var repo = new InMemoryUserRepository(user);
        var service = NewService(repo, new StubEntraAssociation());

        Func<Task> act = async () =>
            await service.AddIdentityLinkAsync(userId, IdentitySource.Entra, SecondOid, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<IdentityLinkException>();
        ex.Which.Code.Should().Be(IdentityLinkErrorCodes.EntraLinkAlreadyPresent);
        repo.IdentityLinkAddCount.Should().Be(0);
    }

    [Fact]
    public async Task AddIdentityLinkAsync_for_entra_persists_a_pending_link_with_tenant_metadata()
    {
        var userId = new UserId("usr-pending");
        var repo = new InMemoryUserRepository(
            new User(userId, "Operator", [new IdentityLink(IdentitySource.Local, "operator", null)], [], false));
        var service = NewService(repo, new StubEntraAssociation());

        await service.AddIdentityLinkAsync(userId, IdentitySource.Entra, FirstOid, CancellationToken.None);

        var added = repo.AddedLinks.Should().ContainSingle().Subject;
        added.LastAuthenticatedAt.Should().BeNull();
        added.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public async Task RemoveIdentityLinkAsync_will_not_leave_the_last_admin_with_only_a_pending_entra_link()
    {
        var userId = new UserId("usr-last-admin");
        var repo = new InMemoryUserRepository(new User(
            userId,
            "Only admin",
            [
                new IdentityLink(IdentitySource.Local, "operator", DateTimeOffset.UtcNow),
                new IdentityLink(IdentitySource.Entra, FirstOid, LastAuthenticatedAt: null, TenantId: TenantId)
            ],
            [new RoleId(BootstrapConstants.PlatformAdminRoleId)],
            false));
        var service = NewService(repo, new StubEntraAssociation(), soleAdmin: true);

        Func<Task> act = async () =>
            await service.RemoveIdentityLinkAsync(userId, IdentitySource.Local, "operator", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<IdentityLinkException>();
        ex.Which.Code.Should().Be(IdentityLinkErrorCodes.LastViableSignInMethod);
    }

    [Fact]
    public async Task RemoveIdentityLinkAsync_allows_removal_when_the_entra_link_is_proven_and_usable()
    {
        var userId = new UserId("usr-last-admin-proven");
        var repo = new InMemoryUserRepository(new User(
            userId,
            "Only admin",
            [
                new IdentityLink(IdentitySource.Local, "operator", DateTimeOffset.UtcNow),
                new IdentityLink(IdentitySource.Entra, FirstOid, DateTimeOffset.UtcNow, TenantId)
            ],
            [new RoleId(BootstrapConstants.PlatformAdminRoleId)],
            false));
        var service = NewService(repo, new StubEntraAssociation(), soleAdmin: true);

        Func<Task> act = async () =>
            await service.RemoveIdentityLinkAsync(userId, IdentitySource.Local, "operator", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private const string FirstOid = "33333333-3333-3333-3333-333333333333";
    private const string SecondOid = "44444444-4444-4444-4444-444444444444";
    private const string TenantId = "11111111-1111-1111-1111-111111111111";

    private static UserManagementService NewService(
        InMemoryUserRepository repo,
        IEntraIdentityAssociationService? entraAssociation = null,
        bool soleAdmin = false) =>
        new(
            new StubPrincipalContext(),
            new StubPrincipalToUserResolver(),
            new AllowUserManageAuth(),
            repo,
            new StubRoleRepository(soleAdmin ? 1 : 0),
            new InMemoryResourceDescriptorRegistry(),
            new StubLocalCredentialRepository(),
            new StubPasswordHasher(),
            new StubAuditService(),
            Options.Create(new LocalAuthOptions()),
            entraAssociation,
            entraAssociation is null ? null : new UsableEntraOptionsMonitor());

    /// <summary>Stands in for the real association service: pending link with active-registration tenant.</summary>
    private sealed class StubEntraAssociation : IEntraIdentityAssociationService
    {
        public ValueTask<EntraAssociationOutcome> CompleteSignInAsync(EntraIdentityAssertion assertion, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(EntraAssociationOutcome.Failure(EntraLinkFailureCodes.AccountNotLinked));

        public ValueTask<EntraAssociationOutcome> CompleteLinkAsync(UserId initiatingUserId, EntraIdentityAssertion assertion, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(EntraAssociationOutcome.Success(initiatingUserId));

        public ValueTask<IdentityLink> BuildPendingAdminLinkAsync(string objectId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new IdentityLink(
                IdentitySource.Entra,
                objectId.Trim().ToLowerInvariant(),
                LastAuthenticatedAt: null,
                TenantId: TenantId,
                RegistrationId: "reg-1",
                LinkedAt: DateTimeOffset.UtcNow));
    }

    private sealed class UsableEntraOptionsMonitor : IOptionsMonitor<EntraAuthOptions>
    {
        public EntraAuthOptions CurrentValue { get; } = new()
        {
            Enabled = true,
            Authority = "https://login.microsoftonline.com/" + TenantId,
            ClientId = "client-1",
            ClientSecret = "secret"
        };

        public EntraAuthOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<EntraAuthOptions, string?> listener) => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class InMemoryUserRepository : IUserRepository
    {
        private readonly Dictionary<UserId, User> _users;

        public InMemoryUserRepository(params User[] users) =>
            _users = users.ToDictionary(u => u.Id);

        public int IdentityLinkAddCount { get; private set; }

        public List<IdentityLink> AddedLinks { get; } = [];

        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Count);

        public ValueTask<User?> GetByIdAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.GetValueOrDefault(userId));

        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<(IReadOnlyList<User>, int)>((_users.Values.ToList(), _users.Count));

        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default)
        {
            var match = _users.Values.FirstOrDefault(u =>
                u.IdentityLinks.Any(l => l.Source == source && l.ExternalId == externalId));
            return ValueTask.FromResult(match);
        }

        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default)
        {
            _users[user.Id] = user;
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default)
        {
            _users[user.Id] = user;
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            AddIdentityLinkAsync(userId, new IdentityLink(source, externalId, null), cancellationToken);

        public ValueTask AddIdentityLinkAsync(UserId userId, IdentityLink link, CancellationToken cancellationToken = default)
        {
            IdentityLinkAddCount++;
            AddedLinks.Add(link);
            var user = _users[userId];
            var links = user.IdentityLinks.ToList();
            links.Add(link);
            _users[userId] = user with { IdentityLinks = links };
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class AllowUserManageAuth : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(IActor actor, PermissionDescriptor permission, ResourceDescriptor? resource, IGovernedResourceIdentity? resourceIdentity = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new HumanActor(new ActorId("admin"), "admin", isBreakGlassSession: false));
    }

    private sealed class StubPrincipalToUserResolver : IPrincipalToUserResolver
    {
        public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<UserId?>(new UserId("admin"));
    }

    private sealed class StubRoleRepository : IRoleRepository
    {
        private readonly int _platformAdminAssignmentCount;

        public StubRoleRepository(int platformAdminAssignmentCount = 0) =>
            _platformAdminAssignmentCount = platformAdminAssignmentCount;

        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Role?>(null);

        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_platformAdminAssignmentCount);

        public ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class StubLocalCredentialRepository : ILocalCredentialRepository
    {
        public ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<LocalCredentialData?>(null);

        public ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SetDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class StubPasswordHasher : ILocalPasswordHasher
    {
        public string AlgorithmName => "stub";
        public (string Hash, string Salt) HashPassword(string password) => ("h", "s");
        public bool VerifyPassword(string password, string hash, string salt) => true;
    }

    private sealed class StubAuditService : IAuditService
    {
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage([], null));

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AuditRecord?>(null);
    }
}
