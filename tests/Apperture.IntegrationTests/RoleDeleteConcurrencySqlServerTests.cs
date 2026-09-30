using System.Data.Common;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.IntegrationTests;

/// <summary>
/// Real SQL Server proof that a custom role is hard-deleted only when it has zero assignments,
/// and that delete never leaves a <c>UserRoles</c> row behind. In-memory providers cannot take
/// <c>UPDLOCK</c>/<c>HOLDLOCK</c> or enforce the restrict foreign key.
/// </summary>
/// <remarks>
/// Metadata update and grant replacement are not interleaved here. Both write only the
/// <c>Roles</c> row. They cannot insert or delete <c>UserRoles</c>, so they cannot produce a
/// deleted role that still has an assignment. The design gate's required SQL proof is
/// assign-versus-delete. System-role refusal stays a service test because the flag is immutable.
/// </remarks>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class RoleDeleteConcurrencySqlServerTests
{
    private const int LockTimeoutNumber = 1222;
    private const int ForeignKeyViolationNumber = 547;

    private readonly ITestOutputHelper _output;

    public RoleDeleteConcurrencySqlServerTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Delete_holds_assignment_range_lock_then_assignment_fails_because_the_role_is_gone()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_Iam84A_");
        var connectionString = ConnectionStringOf(fixture);
        var (roleId, userId) = await SeedUnassignedRoleAsync(fixture);

        var pause = new UserRolesLockPause();
        var deleteReturned = new TaskCompletionSource<RoleUnassignedDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var deleteTask = Task.Run(async () =>
        {
            await using var db = NewDb(connectionString, pause);
            await db.Database.OpenConnectionAsync();
            await using var tx = await db.Database.BeginTransactionAsync();
            pause.SessionId = await ReadSpidAsync(db);
            var result = await new SqlRoleRepository(db).TryDeleteUnassignedAsync(new RoleId(roleId));
            deleteReturned.TrySetResult(result);
            await allowCommit.Task;
            if (result.Outcome == RoleUnassignedDeleteOutcome.Deleted)
                await tx.CommitAsync();
        });

        try
        {
            var finishedFirst = await Task.WhenAny(pause.Paused, deleteTask).WaitAsync(TimeSpan.FromSeconds(30));
            if (finishedFirst != pause.Paused)
            {
                throw new InvalidOperationException(
                    "Delete finished before the UserRoles lifecycle lock was observed. Commands: "
                    + pause.CommandText());
            }

            var locks = await ReadKeyLocksAsync(fixture, pause.SessionId);
            _output.WriteLine("Locks held after the UserRoles lock query and before the role delete:");
            foreach (var row in locks)
                _output.WriteLine($"  {row.Table} {row.Mode}");

            locks.Should().Contain(row =>
                    row.Table == "UserRoles" && row.Mode.StartsWith("Range", StringComparison.Ordinal),
                "the delete transaction must hold a key-range lock on UserRoles before it removes the role; observed [{0}]",
                FormatLocks(locks));
            locks.Should().NotContain(row => row.Table == "Roles" && row.Mode == "X",
                "the role row must not already be exclusively locked by DELETE, or the assignment block would not prove the assignment-range lock");

            await using (var during = fixture.NewDb())
            {
                (await during.Roles.AsNoTracking().AnyAsync(r => r.Id == roleId)).Should().BeTrue(
                    "the role row is still present while delete is paused before SaveChanges");
                (await during.UserRoles.AsNoTracking().CountAsync(ur => ur.RoleId == roleId)).Should().Be(0);
            }

            await using (var waiter = fixture.NewDb())
            {
                await waiter.Database.OpenConnectionAsync();
                await waiter.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 0");
                var assign = async () => await new SqlRoleRepository(waiter).AssignToUserAsync(new UserId(userId), new RoleId(roleId));
                var blocked = await assign.Should().ThrowAsync<Exception>();
                SqlNumber(blocked.Which).Should().Be(LockTimeoutNumber,
                    "a concurrent assignment must block on the held UserRoles range lock instead of inserting");
            }

            await using (var stillEmpty = fixture.NewDb())
            {
                (await stillEmpty.UserRoles.AsNoTracking().CountAsync(ur => ur.RoleId == roleId)).Should().Be(0);
                (await stillEmpty.Roles.AsNoTracking().AnyAsync(r => r.Id == roleId)).Should().BeTrue();
            }

            pause.Release();
            var deleted = await deleteReturned.Task.WaitAsync(TimeSpan.FromSeconds(30));
            deleted.Outcome.Should().Be(RoleUnassignedDeleteOutcome.Deleted);
            deleted.AssignmentCount.Should().Be(0);

            await using (var uncommitted = fixture.NewDb())
            {
                await uncommitted.Database.OpenConnectionAsync();
                await uncommitted.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 0");
                var readRole = async () => await uncommitted.Roles.AsNoTracking().AnyAsync(r => r.Id == roleId);
                var blockedRead = await readRole.Should().ThrowAsync<Exception>();
                SqlNumber(blockedRead.Which).Should().Be(LockTimeoutNumber,
                    "the uncommitted role delete holds an exclusive lock, so another session waits instead of observing a deleted role with a new assignment");
            }

            allowCommit.TrySetResult();
            await deleteTask.WaitAsync(TimeSpan.FromSeconds(30));

            await using var after = fixture.NewDb();
            (await after.Roles.AsNoTracking().AnyAsync(r => r.Id == roleId)).Should().BeFalse();
            var lateAssign = async () => await new SqlRoleRepository(after).AssignToUserAsync(new UserId(userId), new RoleId(roleId));
            var gone = await lateAssign.Should().ThrowAsync<Exception>();
            SqlNumber(gone.Which).Should().Be(ForeignKeyViolationNumber,
                "once delete commits, assignment fails because the role row is gone");
            (await after.UserRoles.AsNoTracking().AnyAsync(ur => ur.RoleId == roleId)).Should().BeFalse();
        }
        finally
        {
            pause.Release();
            allowCommit.TrySetResult();
            try
            {
                await deleteTask.WaitAsync(TimeSpan.FromSeconds(20));
            }
            catch (Exception ex)
            {
                _output.WriteLine("Delete task ended after cleanup: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    [Fact]
    public async Task Assignment_that_lands_first_makes_delete_refuse_and_keeps_the_assignment()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_Iam84B_");
        var (roleId, userId) = await SeedUnassignedRoleAsync(fixture);

        await using var assigner = fixture.NewDb();
        await assigner.Database.OpenConnectionAsync();
        await using var assignTx = await assigner.Database.BeginTransactionAsync();
        await new SqlRoleRepository(assigner).AssignToUserAsync(new UserId(userId), new RoleId(roleId));

        await using (var deleter = fixture.NewDb())
        {
            await deleter.Database.OpenConnectionAsync();
            await deleter.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 0");
            await using var deleteTx = await deleter.Database.BeginTransactionAsync();
            var blocked = async () => await new SqlRoleRepository(deleter).TryDeleteUnassignedAsync(new RoleId(roleId));
            var blockedEx = await blocked.Should().ThrowAsync<Exception>();
            SqlNumber(blockedEx.Which).Should().Be(LockTimeoutNumber,
                "delete's locked assignment read must wait for the uncommitted UserRoles insert");
            await deleteTx.RollbackAsync();
        }

        await using (var mid = fixture.NewDb())
        {
            (await mid.Roles.AsNoTracking().AnyAsync(r => r.Id == roleId)).Should().BeTrue(
                "the blocked delete must not have removed the role");
        }

        await assignTx.CommitAsync();

        await using var after = fixture.NewDb();
        var refused = await new SqlRoleRepository(after).TryDeleteUnassignedAsync(new RoleId(roleId));
        refused.Outcome.Should().Be(RoleUnassignedDeleteOutcome.AssignmentsRemain);
        refused.AssignmentCount.Should().Be(1);
        (await after.Roles.AsNoTracking().AnyAsync(r => r.Id == roleId)).Should().BeTrue();
        (await after.UserRoles.AsNoTracking().CountAsync(ur => ur.RoleId == roleId && ur.UserId == userId)).Should().Be(1);
    }

    [Fact]
    public async Task Restrict_foreign_key_rejects_a_direct_role_delete_while_an_assignment_exists()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_Iam84Fk_");
        var (roleId, userId) = await SeedUnassignedRoleAsync(fixture);

        await using (var db = fixture.NewDb())
        {
            await db.Database.OpenConnectionAsync();
            string? deleteAction = null;
            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = """
                    SELECT delete_referential_action_desc
                    FROM sys.foreign_keys
                    WHERE name = N'FK_UserRoles_Roles_RoleId'
                      AND OBJECT_NAME(parent_object_id) = N'UserRoles'
                      AND OBJECT_NAME(referenced_object_id) = N'Roles';
                    """;
                var scalar = await command.ExecuteScalarAsync();
                deleteAction = scalar as string;
            }

            deleteAction.Should().Be("NO_ACTION",
                "SQL Server reports ON DELETE RESTRICT as NO_ACTION; that is the backstop under the lifecycle locks");

            await new SqlRoleRepository(db).AssignToUserAsync(new UserId(userId), new RoleId(roleId));

            await using var delete = db.Database.GetDbConnection().CreateCommand();
            delete.CommandText = "DELETE FROM [Roles] WHERE [Id] = @id";
            var id = delete.CreateParameter();
            id.ParameterName = "@id";
            id.Value = roleId;
            delete.Parameters.Add(id);
            var act = async () => await delete.ExecuteNonQueryAsync();
            var ex = await act.Should().ThrowAsync<SqlException>();
            ex.Which.Number.Should().Be(ForeignKeyViolationNumber);
            ex.Which.Message.Should().Contain("FK_UserRoles_Roles_RoleId");
        }

        await using var after = fixture.NewDb();
        (await after.Roles.AsNoTracking().AnyAsync(r => r.Id == roleId)).Should().BeTrue();
        (await after.UserRoles.AsNoTracking().CountAsync(ur => ur.RoleId == roleId && ur.UserId == userId)).Should().Be(1);
    }

    private static async Task<SqlServerScratchDatabase> RequireFixtureAsync(string catalogPrefix)
    {
        var fixture = await SqlServerScratchDatabase.CreateAsync(catalogPrefix);
        fixture.Should().NotBeNull(
            "IAM-8.4 SQL proof requires these tests to run against SQL Server. " +
            SqlServerTestConfiguration.DescribeActiveConfiguration());
        return fixture!;
    }

    private static async Task<(string RoleId, string UserId)> SeedUnassignedRoleAsync(SqlServerScratchDatabase fixture)
    {
        var roleId = "role-" + Guid.NewGuid().ToString("N");
        var userId = "user-" + Guid.NewGuid().ToString("N")[..16];
        await using var db = fixture.NewDb();
        db.Users.Add(new UserEntity
        {
            Id = userId,
            DisplayName = "Pat Lee",
            IsBreakGlassEligible = false
        });
        await new SqlRoleRepository(db).CreateAsync(new Role(
            new RoleId(roleId),
            "Operators " + roleId[^6..],
            null,
            [],
            IsSystemRole: false));
        return (roleId, userId);
    }

    private static string ConnectionStringOf(SqlServerScratchDatabase fixture)
    {
        using var probe = fixture.NewDb();
        return probe.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Scratch catalog connection string was not available.");
    }

    private static AppertureDbContext NewDb(string connectionString, DbCommandInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(connectionString)
            .AddInterceptors(interceptor)
            .Options;
        return new AppertureDbContext(options);
    }

    private static async Task<int> ReadSpidAsync(AppertureDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT @@SPID";
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value);
    }

    private static async Task<IReadOnlyList<KeyLock>> ReadKeyLocksAsync(SqlServerScratchDatabase fixture, int sessionId)
    {
        await using var db = fixture.NewDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT l.request_mode, OBJECT_NAME(p.object_id)
            FROM sys.dm_tran_locks AS l
            INNER JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id
            WHERE l.request_session_id = @spid
              AND l.resource_database_id = DB_ID()
              AND l.resource_type = N'KEY';
            """;
        var spid = command.CreateParameter();
        spid.ParameterName = "@spid";
        spid.Value = sessionId;
        command.Parameters.Add(spid);

        var rows = new List<KeyLock>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new KeyLock(reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1)));
        return rows;
    }

    private static int? SqlNumber(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql)
                return sql.Number;
        }

        return null;
    }

    private static string FormatLocks(IReadOnlyList<KeyLock> locks) =>
        locks.Count == 0
            ? "(none)"
            : string.Join(", ", locks.Select(row => row.Table + ":" + row.Mode));

    private sealed record KeyLock(string Mode, string Table);

    /// <summary>
    /// Pauses the delete transaction after the <c>UserRoles</c> <c>UPDLOCK, HOLDLOCK</c> query has
    /// executed and before EF reads it and deletes the role. That is the window where an assignment
    /// insert is blocked by the range lock rather than by the later exclusive lock on <c>Roles</c>.
    /// </summary>
    private sealed class UserRolesLockPause : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _commands = [];
        private int _armed;

        public Task Paused => _paused.Task;

        public int SessionId { get; set; }

        public void Release() => _release.TrySetResult();

        public string CommandText()
        {
            lock (_commands)
                return string.Join(" || ", _commands);
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            lock (_commands)
                _commands.Add(command.CommandText);

            if (IsUserRolesLifecycleLock(command.CommandText) && Interlocked.Exchange(ref _armed, 1) == 0)
            {
                _paused.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return result;
        }

        private static bool IsUserRolesLifecycleLock(string commandText) =>
            commandText.Contains("UserRoles", StringComparison.Ordinal)
            && commandText.Contains("UPDLOCK", StringComparison.Ordinal)
            && commandText.Contains("HOLDLOCK", StringComparison.Ordinal);
    }
}
