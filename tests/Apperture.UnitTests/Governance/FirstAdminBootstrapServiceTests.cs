using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.ControlPlane.Security;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class FirstAdminBootstrapServiceTests
{
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
        var service = new FirstAdminBootstrapService(users, new PresentRoleRepository());

        var result = await service.TryCreateFirstAdminAsync("First admin", IdentitySource.Local, username, "unique-local-passphrase");

        result.Kind.Should().Be(FirstAdminBootstrapResultKind.DuplicateIdentityLink);
        users.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task TryCreateFirstAdminAsync_assigns_the_normalized_sign_in_name_as_username_and_local_link()
    {
        var users = new CapturingUserRepository();
        var service = new FirstAdminBootstrapService(users, new PresentRoleRepository());

        var result = await service.TryCreateFirstAdminAsync("First admin", IdentitySource.Local, "  Admin  ", "unique-local-passphrase");

        result.Kind.Should().Be(FirstAdminBootstrapResultKind.Success);
        var user = users.Added.Should().ContainSingle().Subject;
        user.Username.Should().Be("admin", "admin is not reserved and is the natural first-admin name");
        user.IdentityLinks.Should().ContainSingle(l => l.Source == IdentitySource.Local)
            .Which.ExternalId.Should().Be(user.Username);
    }

    [Theory]
    [InlineData("jane@contoso.com", UsernameErrorCodes.Invalid)]
    [InlineData("ab", UsernameErrorCodes.Invalid)]
    [InlineData("system", UsernameErrorCodes.Reserved)]
    public async Task TryCreateFirstAdminAsync_refuses_a_name_that_fails_the_username_policy(string username, string code)
    {
        var users = new CapturingUserRepository();
        var service = new FirstAdminBootstrapService(users, new PresentRoleRepository());

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
