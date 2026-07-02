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

    private static UserManagementService NewService(InMemoryUserRepository repo) =>
        new(
            new StubPrincipalContext(),
            new StubPrincipalToUserResolver(),
            new AllowUserManageAuth(),
            repo,
            new StubRoleRepository(),
            new InMemoryResourceDescriptorRegistry(),
            new StubLocalCredentialRepository(),
            new StubPasswordHasher(),
            new StubAuditService(),
            Options.Create(new LocalAuthOptions()));

    private sealed class InMemoryUserRepository : IUserRepository
    {
        private readonly Dictionary<UserId, User> _users;

        public InMemoryUserRepository(params User[] users) =>
            _users = users.ToDictionary(u => u.Id);

        public int IdentityLinkAddCount { get; private set; }

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

        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default)
        {
            IdentityLinkAddCount++;
            var user = _users[userId];
            var links = user.IdentityLinks.ToList();
            links.Add(new IdentityLink(source, externalId, null));
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
        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Role?>(null);

        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

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
