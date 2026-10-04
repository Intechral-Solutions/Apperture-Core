using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.ControlPlane.Security;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class BootstrapPlatformAdminAssignmentTests
{
    [Fact]
    public async Task ExecuteAsync_Skips_WhenDisabled()
    {
        var users = new Mock<IUserRepository>(MockBehavior.Strict);
        var roles = new Mock<IRoleRepository>(MockBehavior.Strict);
        var opts = new BootstrapPlatformAdminOptions { Enabled = false, Principals = { new() { IdentitySource = "Local", ExternalId = "x" } } };

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null);

        users.VerifyNoOtherCalls();
        roles.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_Skips_WhenPrincipalsEmpty()
    {
        var users = new Mock<IUserRepository>(MockBehavior.Strict);
        var roles = new Mock<IRoleRepository>(MockBehavior.Strict);
        var opts = new BootstrapPlatformAdminOptions { Enabled = true, Principals = new() };

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null);

        users.VerifyNoOtherCalls();
        roles.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_AssignsRole_WhenUserExistsWithoutPlatformAdmin()
    {
        var userId = new UserId("user1");
        var user = new User(
            userId,
            "Alice",
            new[] { new IdentityLink(IdentitySource.Local, "alice", null) },
            Array.Empty<RoleId>(),
            false)
        { Username = "alice" };

        var adminRole = new Role(
            new RoleId(BootstrapConstants.PlatformAdminRoleId),
            "Platform Administrator",
            null,
            Array.Empty<PermissionDescriptor>(),
            IsSystemRole: true);

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdentityLinkAsync(IdentitySource.Local, "alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetAsync(It.Is<RoleId>(id => id.Value == BootstrapConstants.PlatformAdminRoleId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminRole);
        roles.Setup(r => r.GetForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Role>());

        var opts = new BootstrapPlatformAdminOptions
        {
            Enabled = true,
            Principals = { new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Local", ExternalId = "alice" } }
        };

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null);

        roles.Verify(
            r => r.AssignToUserAsync(userId, It.Is<RoleId>(rid => rid.Value == BootstrapConstants.PlatformAdminRoleId), It.IsAny<CancellationToken>()),
            Times.Once);
        users.Verify(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_IsIdempotent_SecondRunDoesNotAssignAgain()
    {
        var userId = new UserId("user1");
        var user = new User(
            userId,
            "Alice",
            new[] { new IdentityLink(IdentitySource.Local, "alice", null) },
            new[] { new RoleId(BootstrapConstants.PlatformAdminRoleId) },
            false)
        { Username = "alice" };

        var adminRole = new Role(
            new RoleId(BootstrapConstants.PlatformAdminRoleId),
            "Platform Administrator",
            null,
            Array.Empty<PermissionDescriptor>(),
            IsSystemRole: true);

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdentityLinkAsync(IdentitySource.Local, "alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetAsync(It.Is<RoleId>(id => id.Value == BootstrapConstants.PlatformAdminRoleId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminRole);
        roles.Setup(r => r.GetForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { adminRole });

        var opts = new BootstrapPlatformAdminOptions
        {
            Enabled = true,
            Principals = { new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Local", ExternalId = "alice" } }
        };

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null);
        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null);

        roles.Verify(
            r => r.AssignToUserAsync(It.IsAny<UserId>(), It.IsAny<RoleId>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesUser_WhenMissingAndCreateUserIfMissing()
    {
        var adminRole = new Role(
            new RoleId(BootstrapConstants.PlatformAdminRoleId),
            "Platform Administrator",
            null,
            Array.Empty<PermissionDescriptor>(),
            IsSystemRole: true);

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdentityLinkAsync(IdentitySource.Local, "newuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetAsync(It.Is<RoleId>(id => id.Value == BootstrapConstants.PlatformAdminRoleId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminRole);

        var opts = new BootstrapPlatformAdminOptions
        {
            Enabled = true,
            CreateUserIfMissing = true,
            Principals =
            {
                new BootstrapPlatformAdminPrincipalOptions
                {
                    IdentitySource = "Local",
                    ExternalId = "newuser",
                    DisplayName = "New User"
                }
            }
        };

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null);

        users.Verify(
            u => u.AddAsync(
                It.Is<User>(x =>
                    x.DisplayName == "New User" &&
                    x.IdentityLinks.Any(l => l.Source == IdentitySource.Local && l.ExternalId == "newuser") &&
                    x.AssignedRoleIds.Any(r => r.Value == BootstrapConstants.PlatformAdminRoleId)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        roles.Verify(
            r => r.AssignToUserAsync(It.IsAny<UserId>(), It.IsAny<RoleId>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsCreate_WhenUserMissingAndCreateUserIfMissingFalse()
    {
        var adminRole = new Role(
            new RoleId(BootstrapConstants.PlatformAdminRoleId),
            "Platform Administrator",
            null,
            Array.Empty<PermissionDescriptor>(),
            IsSystemRole: true);

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdentityLinkAsync(IdentitySource.Entra, "oid-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetAsync(It.Is<RoleId>(id => id.Value == BootstrapConstants.PlatformAdminRoleId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminRole);

        var opts = new BootstrapPlatformAdminOptions
        {
            Enabled = true,
            CreateUserIfMissing = false,
            Principals =
            {
                new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Entra", ExternalId = "oid-123" }
            }
        };

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null);

        users.Verify(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        roles.Verify(
            r => r.AssignToUserAsync(It.IsAny<UserId>(), It.IsAny<RoleId>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private const string BootstrapTid = "11111111-2222-3333-4444-555555555555";
    private const string BootstrapOid = "66666666-7777-8888-9999-000000000000";

    [Theory]
    [InlineData(null, "user")] // default display text carries the oid and is never a username source
    [InlineData("Pat Admin", "pat.admin")]
    public async Task ExecuteAsync_CreatesEntraBootstrapAdmin_WithGeneratedUsername(string? displayName, string expected)
    {
        var (users, roles, added) = CreatingRepositories();
        var opts = new BootstrapPlatformAdminOptions
        {
            Enabled = true,
            CreateUserIfMissing = true,
            Principals =
            {
                new BootstrapPlatformAdminPrincipalOptions
                {
                    IdentitySource = "Entra", ExternalId = BootstrapOid, TenantId = BootstrapTid, DisplayName = displayName
                }
            }
        };

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null);

        var user = added.Should().ContainSingle().Subject;
        user.Username.Should().Be(expected);
        user.Username.Should().NotContain(BootstrapOid[..8]);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesLocalBootstrapAdmin_WithTheSignInNameAsUsername()
    {
        var (users, roles, added) = CreatingRepositories();
        var opts = new BootstrapPlatformAdminOptions
        {
            Enabled = true,
            CreateUserIfMissing = true,
            Principals = { new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Local", ExternalId = "  Ops.Admin " } }
        };

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null);

        var user = added.Should().ContainSingle().Subject;
        user.Username.Should().Be("ops.admin");
        user.IdentityLinks.Single(l => l.Source == IdentitySource.Local).ExternalId.Should().Be("ops.admin");
    }

    [Theory]
    [InlineData("ops@contoso.com")]
    [InlineData("system")]
    public async Task ExecuteAsync_SkipsLocalBootstrapAdmin_WhoseNameFailsTheUsernamePolicy(string externalId)
    {
        var (users, roles, added) = CreatingRepositories();
        var opts = new BootstrapPlatformAdminOptions
        {
            Enabled = true,
            CreateUserIfMissing = true,
            Principals = { new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Local", ExternalId = externalId } }
        };

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null);

        added.Should().BeEmpty();
    }

    private static (Mock<IUserRepository> Users, Mock<IRoleRepository> Roles, List<User> Added) CreatingRepositories()
    {
        var added = new List<User>();
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((user, _) => added.Add(user))
            .Returns(ValueTask.CompletedTask);
        users.Setup(u => u.FindUsernameLedgerEntryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UsernameLedgerEntry?)null);

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetAsync(It.Is<RoleId>(id => id.Value == BootstrapConstants.PlatformAdminRoleId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Role(
                new RoleId(BootstrapConstants.PlatformAdminRoleId),
                "Platform Administrator",
                null,
                Array.Empty<PermissionDescriptor>(),
                IsSystemRole: true));
        return (users, roles, added);
    }
}
