using System.Data.Common;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// IAM-10.2a slice 2 against real SQL Server: the authority snapshot's lock set (IAM-10 gate P-5), the rules decided from it
/// (P-2 to P-4), unreadable role permissions (P-1), and, in the other parts of this class, the race cases RT-1 to RT-9 and the
/// audited-unit failure cases. Every case runs on a catalog with the default locking READ COMMITTED and on one with
/// READ_COMMITTED_SNAPSHOT, because a hinted read takes locks under both but an unhinted one does not. Fails, never skips, without
/// SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed partial class RoleAuthoritySqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public RoleAuthoritySqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    /// <summary>A failing case must not leave an operation parked with its locks: release every pause the case made.</summary>
    public void Dispose() => PauseAt.ReleaseAll();

    private RoleAuthorityWorld World(bool rcsi) => new(_catalogs.For(rcsi));

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    // ---------------------------------------------------------------------------------------------------------------
    // The lock set (P-5)
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_snapshot_refuses_to_run_outside_a_transaction(bool rcsi)
    {
        var world = World(rcsi);
        var actor = await world.NewUserAsync("actor");
        await using var db = world.Scratch.NewDb();

        var act = async () => await new SqlRoleRepository(db).LockAuthoritySnapshotAsync(new UserId(actor), null, null);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*open transaction*");
        _output.WriteLine(world.Scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_snapshot_returns_the_actors_roles_the_target_role_and_whether_the_assignment_exists_as_locked(bool rcsi)
    {
        var world = World(rcsi);
        var roleA = await world.NewRoleAsync("a", P);
        var roleB = await world.NewRoleAsync("b", Q);
        var target = await world.NewRoleAsync("target", S);
        var actor = await world.NewUserAsync("actor", roleA, roleB);
        var held = await world.NewUserAsync("held", target);
        var empty = await world.NewUserAsync("empty");

        await using var db = world.NewPlainDb();
        await using var tx = await db.Database.BeginTransactionAsync();
        var repository = new SqlRoleRepository(db);

        var withHolder = await repository.LockAuthoritySnapshotAsync(new UserId(actor), new RoleId(target), new UserId(held));
        var withoutHolder = await repository.LockAuthoritySnapshotAsync(new UserId(actor), new RoleId(target), new UserId(empty));
        var missing = await repository.LockAuthoritySnapshotAsync(new UserId(actor), new RoleId("role-does-not-exist"), new UserId(empty));
        var noTarget = await repository.LockAuthoritySnapshotAsync(new UserId(actor), null, null);
        var noActor = await repository.LockAuthoritySnapshotAsync(null, new RoleId(target), null);

        withHolder.ActorRoles.Select(r => r.Id.Value).Should().BeEquivalentTo([roleA, roleB]);
        withHolder.ActorRoles.Single(r => r.Id.Value == roleA).Permissions.Should().Equal(P);
        withHolder.TargetRole!.Id.Value.Should().Be(target);
        withHolder.TargetRole.Permissions.Should().Equal(S);
        withHolder.TargetAssignmentExists.Should().BeTrue();
        withoutHolder.TargetAssignmentExists.Should().BeFalse();
        missing.TargetRole.Should().BeNull();
        missing.TargetAssignmentExists.Should().BeFalse();
        noTarget.TargetRole.Should().BeNull();
        noActor.ActorRoles.Should().BeEmpty();
        noActor.TargetRole!.Id.Value.Should().Be(target);
        await tx.RollbackAsync();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_snapshot_takes_its_locks_in_the_approved_order_with_each_role_row_once_in_ascending_id_order(bool rcsi)
    {
        var world = World(rcsi);
        // Ids chosen so that ascending order is not creation order, and so the target is not among the actor's roles.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var rZ = $"role-z-{suffix}";
        var rA = $"role-a-{suffix}";
        var rM = $"role-m-{suffix}";
        foreach (var id in new[] { rZ, rA, rM })
            await world.Scratch.ExecuteAsync($"INSERT INTO Roles (Id, Name, Description, PermissionsJson, IsSystemRole) VALUES (N'{id}', N'{id}', NULL, N'[]', 0);");
        var actor = await world.NewUserAsync("actor", rZ, rA);
        var user = await world.NewUserAsync("user");

        var log = new StatementLog();
        await using var db = world.NewPlainDb(log);
        await using var tx = await db.Database.BeginTransactionAsync();

        await new SqlRoleRepository(db).LockAuthoritySnapshotAsync(new UserId(actor), new RoleId(rM), new UserId(user));

        var locks = log.Statements.Where(s => s.Sql.Contains("UPDLOCK, HOLDLOCK", StringComparison.Ordinal)).ToList();
        locks.Should().HaveCount(5, "the actor's assignments, three role rows (the target once), and the assignment key");
        locks[0].Sql.Should().Contain("FROM [UserRoles]").And.NotContain("AND [RoleId]");
        locks[0].Parameters.Should().Equal(actor);
        locks.Skip(1).Take(3).Select(l => l.Sql).Should().OnlyContain(s => s.Contains("FROM [Roles]"));
        locks.Skip(1).Take(3).SelectMany(l => l.Parameters).Should().Equal([rA, rM, rZ], "role rows are locked in ascending ordinal id order");
        locks[4].Sql.Should().Contain("FROM [UserRoles]").And.Contain("AND [RoleId]");
        locks[4].Parameters.Should().Equal(user, rM);
        await tx.RollbackAsync();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_role_that_is_both_held_by_the_actor_and_the_target_is_locked_once(bool rcsi)
    {
        var world = World(rcsi);
        var shared = await world.NewRoleAsync("shared", P);
        var actor = await world.NewUserAsync("actor", shared);
        var user = await world.NewUserAsync("user");

        var log = new StatementLog();
        await using var db = world.NewPlainDb(log);
        await using var tx = await db.Database.BeginTransactionAsync();

        var snapshot =await new SqlRoleRepository(db).LockAuthoritySnapshotAsync(new UserId(actor), new RoleId(shared), new UserId(user));

        log.Statements.Count(s => s.Sql.Contains("FROM [Roles] WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal)).Should().Be(1, "a self-edit locks one row once");
        snapshot.ActorRoles.Should().ContainSingle().Which.Id.Value.Should().Be(shared);
        snapshot.TargetRole!.Id.Value.Should().Be(shared);
        await tx.RollbackAsync();
    }

    /// <summary>
    /// One competing writer for each kind of row the decision read, each shown by the server's own wait list to be waiting for the
    /// transaction that holds the snapshot, and then let through when that transaction ends. Ends the claim "no other transaction can
    /// insert, update or delete any row, or any key in any range, that fed the decision".
    /// </summary>
    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_snapshot_blocks_a_competing_change_to_every_row_it_read_until_the_transaction_ends(bool rcsi)
    {
        var world = World(rcsi);
        var roleA = await world.NewRoleAsync("a", P);
        var target = await world.NewRoleAsync("target", Q);
        var extra = await world.NewRoleAsync("extra", S);
        var actor = await world.NewUserAsync("actor", roleA);
        var user = await world.NewUserAsync("user");

        var attempts = new (string Name, string Table, string Sql)[]
        {
            ("the actor's assignment is deleted", "UserRoles", $"DELETE FROM UserRoles WHERE UserId = N'{actor}' AND RoleId = N'{roleA}';"),
            ("a new assignment is inserted into the actor's key range", "UserRoles", $"INSERT INTO UserRoles (UserId, RoleId) VALUES (N'{actor}', N'{extra}');"),
            ("a role the actor holds is edited", "Roles", $"UPDATE Roles SET PermissionsJson = N'[]' WHERE Id = N'{roleA}';"),
            ("the target role is edited", "Roles", $"UPDATE Roles SET PermissionsJson = N'[]' WHERE Id = N'{target}';"),
            ("the target assignment key is inserted", "UserRoles", $"INSERT INTO UserRoles (UserId, RoleId) VALUES (N'{user}', N'{target}');"),
        };

        foreach (var (name, table, sql) in attempts)
        {
            await using var db = world.NewPlainDb();
            await using var tx = await db.Database.BeginTransactionAsync();
            await new SqlRoleRepository(db).LockAuthoritySnapshotAsync(new UserId(actor), new RoleId(target), new UserId(user));

            await using var session = await RawSession.BeginAsync(world.Scratch.ConnectionString);
            var write = Task.Run(() => session.ExecuteAsync(sql));
            await world.WaitUntilBlockedOnAsync(table, write);
            write.IsCompleted.Should().BeFalse(name);

            await tx.RollbackAsync();
            await write.WaitAsync(TimeSpan.FromSeconds(30));
            await session.CommitAsync();

            // Put the rows back as the snapshot found them, for the next kind.
            await world.Scratch.ExecuteAsync($"DELETE FROM UserRoles WHERE UserId = N'{user}' AND RoleId = N'{target}';");
            await world.Scratch.ExecuteAsync($"DELETE FROM UserRoles WHERE UserId = N'{actor}' AND RoleId = N'{extra}';");
            await world.Scratch.ExecuteAsync($"IF NOT EXISTS (SELECT 1 FROM UserRoles WHERE UserId = N'{actor}' AND RoleId = N'{roleA}') INSERT INTO UserRoles (UserId, RoleId) VALUES (N'{actor}', N'{roleA}');");
            await world.Scratch.ExecuteAsync($"UPDATE Roles SET PermissionsJson = N'{StoredRolePermissions.Serialize([P])}' WHERE Id = N'{roleA}';");
            await world.Scratch.ExecuteAsync($"UPDATE Roles SET PermissionsJson = N'{StoredRolePermissions.Serialize([Q])}' WHERE Id = N'{target}';");
        }

        _output.WriteLine(world.Scratch.ExecutionMarker);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Unreadable role permissions (P-1), through the real parser and the real repository
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_role_with_unreadable_stored_permissions_is_flagged_and_contributes_no_grants_while_other_roles_still_do(bool rcsi)
    {
        var world = World(rcsi);
        var broken = await world.NewRawRoleAsync("broken", "{not json");
        var halfReadable = await world.NewRawRoleAsync(
            "half", """[{"action":"connection.read","resource":{"resourceType":"Connection","scopeKey":"instance"}},null]""");
        var fine = await world.NewRoleAsync("fine", Q);
        var actor = await world.NewUserAsync("actor", broken, halfReadable, fine);

        await using var db = world.Scratch.NewDb();
        var roles = await new SqlRoleRepository(db).GetForUserAsync(new UserId(actor));

        roles.Single(r => r.Id.Value == broken).PermissionsReadable.Should().BeFalse();
        roles.Single(r => r.Id.Value == halfReadable).PermissionsReadable.Should().BeFalse("a list with an unreadable element is unreadable");
        roles.Single(r => r.Id.Value == halfReadable).Permissions.Should().BeEmpty("the readable part of a damaged list is not salvaged");
        roles.Single(r => r.Id.Value == fine).PermissionsReadable.Should().BeTrue();
        var set = EffectivePermissionSet.FromRoles(roles, new ScopeContainmentEvaluator());
        set.Allows(Q, "instance").Should().BeTrue("the readable role still contributes");
        set.Allows(P, "instance").Should().BeFalse("the half-readable role's readable entry is not honoured");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Recording statements (the order of locks)
    // ---------------------------------------------------------------------------------------------------------------

    private sealed class StatementLog : DbCommandInterceptor
    {
        public List<(string Sql, List<string> Parameters)> Statements { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Statements.Add((command.CommandText, command.Parameters.Cast<DbParameter>().Select(p => p.Value?.ToString() ?? "").ToList()));
            return ValueTask.FromResult(result);
        }
    }
}
