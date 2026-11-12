using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.UnitTests.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.ControlPlane.Security;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class BootstrapPlatformAdminAssignmentTests
{
    private readonly RecordingAuditedUnitOfWork _unit = new();
    private readonly Apperture.UnitTests.AdministratorViability.RecordingGate _gate = new();
    private readonly Apperture.ControlPlane.Security.AdministratorViability.IAdministratorViabilityCoordinator _viability;

    public BootstrapPlatformAdminAssignmentTests() =>
        _viability = Apperture.UnitTests.AdministratorViability.FakeViability.Scripted(_gate);

    [Fact]
    public async Task ExecuteAsync_Skips_WhenDisabled()
    {
        var users = new Mock<IUserRepository>(MockBehavior.Strict);
        var roles = new Mock<IRoleRepository>(MockBehavior.Strict);
        var opts = new BootstrapPlatformAdminOptions { Enabled = false, Principals = { new() { IdentitySource = "Local", ExternalId = "x" } } };

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null, _unit, _viability);

        users.VerifyNoOtherCalls();
        roles.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_Skips_WhenPrincipalsEmpty()
    {
        var users = new Mock<IUserRepository>(MockBehavior.Strict);
        var roles = new Mock<IRoleRepository>(MockBehavior.Strict);
        var opts = new BootstrapPlatformAdminOptions { Enabled = true, Principals = new() };

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null, _unit, _viability);

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

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null, _unit, _viability);

        roles.Verify(
            r => r.AssignToUserAsync(userId, It.Is<RoleId>(rid => rid.Value == BootstrapConstants.PlatformAdminRoleId), It.IsAny<CancellationToken>()),
            Times.Once);
        users.Verify(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);

        // AUD-1.2: the assignment commits with its row, recorded as the startup configuration.
        _unit.Actions.Should().Equal(AuditActionNames.RoleAssigned);
        _unit.Operations.Should().ContainSingle().Which.Operation.Actor.RecordedValue.Should().Be(ActorLiterals.SystemBootstrapConfiguration);
        _unit.Single(AuditActionNames.RoleAssigned).Target!.Id.Should().Be(userId.Value);
        _gate.Acquired.Should().Be(1, "the assignment to an existing user is decided under the gate as well");
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

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null, _unit, _viability);
        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null, _unit, _viability);

        roles.Verify(
            r => r.AssignToUserAsync(It.IsAny<UserId>(), It.IsAny<RoleId>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unit.Staged.Should().BeEmpty("a principal that already holds the role changes nothing and writes no row (D7)");
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

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null, _unit, _viability);

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

        // AUD-1.2: the created administrator and its role commit with their rows; no password or address is recorded.
        _unit.Actions.Should().Equal(AuditActionNames.ConfiguredAdminCreated, AuditActionNames.RoleAssigned);
        _unit.Operations.Should().ContainSingle().Which.Operation.Actor.RecordedValue.Should().Be(ActorLiterals.SystemBootstrapConfiguration);
        var created = _unit.Single(AuditActionNames.ConfiguredAdminCreated);
        created.Fields["username"].Should().Be("newuser");
        created.Fields["identitySource"].Should().Be("local");
        created.Fields["roleId"].Should().Be(BootstrapConstants.PlatformAdminRoleId);
    }

    [Fact]
    public async Task ExecuteAsync_records_a_created_administrator_whose_acknowledgement_was_lost_once_from_the_committed_row()
    {
        var (users, roles, added) = CreatingRepositories();
        _unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;
        var opts = new BootstrapPlatformAdminOptions
        {
            Enabled = true,
            CreateUserIfMissing = true,
            Principals = { new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Local", ExternalId = "ops.admin" } }
        };

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null, _unit, _viability);

        added.Should().ContainSingle("verification confirmed the commit, so nothing reran");
        _unit.Actions.Should().Equal(AuditActionNames.ConfiguredAdminCreated, AuditActionNames.RoleAssigned);
    }

    [Fact]
    public async Task ExecuteAsync_requires_the_audited_unit_of_work_before_changing_anything()
    {
        var (users, roles, added) = CreatingRepositories();
        var opts = new BootstrapPlatformAdminOptions
        {
            Enabled = true,
            CreateUserIfMissing = true,
            Principals = { new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Local", ExternalId = "ops.admin" } }
        };

        var act = () => BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
        added.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_takes_the_gate_in_each_principals_unit_before_it_looks_the_principal_up()
    {
        var (users, roles, added) = CreatingRepositories();
        var gateHeldAtEachLookup = new List<int>();
        users.Setup(u => u.GetByIdentityLinkAsync(IdentitySource.Local, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => gateHeldAtEachLookup.Add(_gate.Acquired))
            .ReturnsAsync((User?)null);
        var opts = new BootstrapPlatformAdminOptions
        {
            Enabled = true,
            CreateUserIfMissing = true,
            Principals =
            {
                new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Local", ExternalId = "ops.one" },
                new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Local", ExternalId = "ops.two" },
            }
        };

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null, _unit, _viability);

        gateHeldAtEachLookup.Should().Equal([1, 2], "each principal is decided in a unit of its own, and the gate is taken before the lookup that decides it");
        added.Should().HaveCount(2);
        _gate.Acquired.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_takes_no_gate_for_a_principal_that_is_skipped_before_any_unit_opens()
    {
        var (users, roles, added) = CreatingRepositories();
        var skipped = new BootstrapPlatformAdminOptions
        {
            Enabled = true,
            CreateUserIfMissing = true,
            Principals =
            {
                new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Local", ExternalId = " " },
                new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Saml", ExternalId = "someone" },
                new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Local", ExternalId = "breakglass" },
            }
        };

        await BootstrapPlatformAdminAssignment.ExecuteAsync(skipped, users.Object, roles.Object, null, _unit, _viability);

        _gate.Acquired.Should().Be(0, "an entry that cannot be applied is skipped before any unit opens, so it queues behind nothing");
        _unit.Actions.Should().BeEmpty();
        added.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_lets_a_busy_gate_fail_the_step_and_creates_nothing()
    {
        var (users, roles, added) = CreatingRepositories();
        var busy = new Apperture.Core.Application.AdministratorViability.PlatformAdministratorSetBusyException(
            Apperture.Core.Application.AdministratorViability.PlatformAdministratorSetLockOutcome.TimedOut);
        _gate.Fail = busy;
        var opts = new BootstrapPlatformAdminOptions
        {
            Enabled = true,
            CreateUserIfMissing = true,
            Principals = { new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Local", ExternalId = "ops.admin" } }
        };

        var act = () => BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null, _unit, _viability);

        (await act.Should().ThrowAsync<Apperture.Core.Application.AdministratorViability.PlatformAdministratorSetBusyException>())
            .Which.Should().BeSameAs(busy, "a start must not silently skip an administrator it was configured to create");
        added.Should().BeEmpty();
        _unit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_requires_the_viability_coordinator_before_changing_anything()
    {
        var (users, roles, added) = CreatingRepositories();
        var opts = new BootstrapPlatformAdminOptions
        {
            Enabled = true,
            CreateUserIfMissing = true,
            Principals = { new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Local", ExternalId = "ops.admin" } }
        };

        var act = () => BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null, _unit);

        await act.Should().ThrowAsync<InvalidOperationException>("the gate is not optional");
        added.Should().BeEmpty();
        _unit.Actions.Should().BeEmpty();
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

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null, _unit, _viability);

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

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null, _unit, _viability);

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

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null, _unit, _viability);

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

        await BootstrapPlatformAdminAssignment.ExecuteAsync(opts, users.Object, roles.Object, null, _unit, _viability);

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
