using Apperture.ControlPlane.Security;
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
/// Validates that role permission assignment rejects unknown/malformed permissions.
/// </summary>
public sealed class RolePermissionAssignmentValidationTests
{
    [Fact]
    public async Task SetRolePermissionsAsync_throws_when_permission_not_in_catalog()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        registry.RegisterPermission(
            new PermissionDescriptor("user.read", new ResourceDescriptor("User", "instance")),
            ResourceFamily.Core);

        var userManagement = new UserManagementService(
            new StubPrincipalContext(),
            new StubPrincipalToUserResolver(),
            new AllowPermissionAssignAuth(),
            new StubUserRepository(),
            new StubRoleRepository(),
            registry,
            new StubLocalCredentialRepository(),
            new StubPasswordHasher(),
            new StubAuditService(),
            Options.Create(new LocalAuthOptions()));

        var unknownPermission = new PermissionDescriptor(
            "unknown.action",
            new ResourceDescriptor("UnknownType", "instance"));

        var act = async () => await userManagement.SetRolePermissionsAsync(
            new RoleId("role-1"),
            [unknownPermission]);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not in the canonical governance catalog*");
    }

    [Fact]
    public async Task SetRolePermissionsAsync_succeeds_when_all_permissions_in_catalog()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        registry.RegisterPermission(
            new PermissionDescriptor("user.read", new ResourceDescriptor("User", "instance")),
            ResourceFamily.Core);

        var roleRepo = new StubRoleRepository();
        var userManagement = new UserManagementService(
            new StubPrincipalContext(),
            new StubPrincipalToUserResolver(),
            new AllowPermissionAssignAuth(),
            new StubUserRepository(),
            roleRepo,
            registry,
            new StubLocalCredentialRepository(),
            new StubPasswordHasher(),
            new StubAuditService(),
            Options.Create(new LocalAuthOptions()));

        var knownPermission = new PermissionDescriptor(
            "user.read",
            new ResourceDescriptor("User", "instance"));

        await userManagement.SetRolePermissionsAsync(
            new RoleId("role-1"),
            [knownPermission]);

        roleRepo.LastUpdatedPermissions.Should().ContainSingle();
        roleRepo.LastUpdatedPermissions![0].Action.Should().Be("user.read");
    }

    [Fact]
    public async Task SetRolePermissionsAsync_allows_empty_list()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var roleRepo = new StubRoleRepository();
        var userManagement = new UserManagementService(
            new StubPrincipalContext(),
            new StubPrincipalToUserResolver(),
            new AllowPermissionAssignAuth(),
            new StubUserRepository(),
            roleRepo,
            registry,
            new StubLocalCredentialRepository(),
            new StubPasswordHasher(),
            new StubAuditService(),
            Options.Create(new LocalAuthOptions()));

        await userManagement.SetRolePermissionsAsync(new RoleId("role-1"), []);

        roleRepo.LastUpdatedPermissions.Should().NotBeNull();
        roleRepo.LastUpdatedPermissions!.Should().BeEmpty();
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false));
    }

    private sealed class StubPrincipalToUserResolver : IPrincipalToUserResolver
    {
        public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<UserId?>(new UserId("user-1"));
    }

    private sealed class AllowPermissionAssignAuth : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(IActor actor, PermissionDescriptor permission, ResourceDescriptor? resource, IGovernedResourceIdentity? resourceIdentity = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class StubUserRepository : IUserRepository
    {
        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask<User?> GetByIdAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<User?>(null);
        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) => ValueTask.FromResult<(IReadOnlyList<User>, int)>((Array.Empty<User>(), 0));
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.FromResult<User?>(null);
        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class StubRoleRepository : IRoleRepository
    {
        public IReadOnlyList<PermissionDescriptor>? LastUpdatedPermissions { get; private set; }

        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Role?>(new Role(roleId, "Test", null, [], false));

        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default)
        {
            LastUpdatedPermissions = permissions;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubLocalCredentialRepository : ILocalCredentialRepository
    {
        public ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<LocalCredentialData?>(null);
        public ValueTask SetDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class StubPasswordHasher : ILocalPasswordHasher
    {
        public string AlgorithmName => "stub";
        public (string Hash, string Salt) HashPassword(string password) => ("", "");
        public bool VerifyPassword(string password, string hash, string salt) => false;
    }

    private sealed class StubAuditService : IAuditService
    {
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage([], null));
    }
}
