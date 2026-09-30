using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class SqlRoleRepositoryLifecycleTests
{
    [Fact]
    public async Task CreateAsync_writes_role_and_initial_grants_as_one_row()
    {
        await using var db = NewDb();
        var repo = new SqlRoleRepository(db);
        var grant = new PermissionDescriptor("user.read", new ResourceDescriptor("User", "instance"));
        var role = new Role(new RoleId("role-" + new string('1', 32)), "Operators", "Team", [grant], IsSystemRole: true);

        await repo.CreateAsync(role);

        var stored = await repo.GetAsync(role.Id);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be("Operators");
        stored.Description.Should().Be("Team");
        stored.IsSystemRole.Should().BeFalse();
        stored.Permissions.Should().ContainSingle(p => p.Action == "user.read");
    }

    [Fact]
    public async Task TryDeleteUnassignedAsync_deletes_only_when_assignment_count_is_zero()
    {
        await using var db = NewDb();
        var repo = new SqlRoleRepository(db);
        var role = new Role(new RoleId("role-" + new string('2', 32)), "Operators", null, [], false);
        await repo.CreateAsync(role);

        var deleted = await repo.TryDeleteUnassignedAsync(role.Id);

        deleted.Outcome.Should().Be(RoleUnassignedDeleteOutcome.Deleted);
        (await repo.GetAsync(role.Id)).Should().BeNull();
    }

    [Fact]
    public async Task TryDeleteUnassignedAsync_refuses_when_assignments_exist_and_does_not_remove_them()
    {
        await using var db = NewDb();
        SeedUser(db, "user-1");
        var repo = new SqlRoleRepository(db);
        var role = new Role(new RoleId("role-" + new string('3', 32)), "Operators", null, [], false);
        await repo.CreateAsync(role);
        await repo.AssignToUserAsync(new UserId("user-1"), role.Id);

        var refused = await repo.TryDeleteUnassignedAsync(role.Id);

        refused.Outcome.Should().Be(RoleUnassignedDeleteOutcome.AssignmentsRemain);
        refused.AssignmentCount.Should().Be(1);
        (await repo.GetAsync(role.Id)).Should().NotBeNull();
        (await repo.CountUserAssignmentsAsync(role.Id)).Should().Be(1);
    }

    [Fact]
    public async Task UpdateMetadataAsync_replaces_name_and_description_only()
    {
        await using var db = NewDb();
        var repo = new SqlRoleRepository(db);
        var grant = new PermissionDescriptor("user.read", new ResourceDescriptor("User", "instance"));
        var role = new Role(new RoleId("role-" + new string('4', 32)), "Operators", "Team", [grant], false);
        await repo.CreateAsync(role);

        await repo.UpdateMetadataAsync(role.Id, "Reviewers", null);

        var stored = await repo.GetAsync(role.Id);
        stored!.Name.Should().Be("Reviewers");
        stored.Description.Should().BeNull();
        stored.Permissions.Should().ContainSingle(p => p.Action == "user.read");
        stored.IsSystemRole.Should().BeFalse();
    }

    private static AppertureDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static void SeedUser(AppertureDbContext db, string userId)
    {
        db.Users.Add(new UserEntity
        {
            Id = userId,
            DisplayName = "Pat Lee",
            IsBreakGlassEligible = false
        });
        db.SaveChanges();
    }
}
