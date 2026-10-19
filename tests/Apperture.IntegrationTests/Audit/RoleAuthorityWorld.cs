using System.Data.Common;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// People, roles and assignments on a throwaway catalog, and the production user administration service built over it, for
/// the SQL Server suites of the anti-escalation rules (IAM-10.2a). Every name is unique, so one catalog serves many cases.
/// </summary>
internal sealed class RoleAuthorityWorld
{
    public static readonly PermissionDescriptor P = Grant("connection.read", "Connection");
    public static readonly PermissionDescriptor Q = Grant("integration.read", "Integration");
    public static readonly PermissionDescriptor S = Grant("schedule.manage", "Schedule");
    public static readonly PermissionDescriptor RoleAssign = Grant("role.assign", "Role");
    public static readonly PermissionDescriptor PermissionAssign = Grant("permission.assign", "Role");
    public static readonly PermissionDescriptor RoleManage = Grant("role.manage", "Role");

    public const string AdminRoleId = "role-platform-administrator";

    private static readonly InMemoryResourceDescriptorRegistry Registry = BuildRegistry();

    /// <summary>The platform permission catalog the service validates grants against.</summary>
    public static IResourceDescriptorRegistry CatalogRegistry => Registry;

    public RoleAuthorityWorld(AuditSqlScratch scratch) => Scratch = scratch;

    public AuditSqlScratch Scratch { get; }

    public static PermissionDescriptor Grant(string action, string resourceType, string scope = "instance") =>
        new(action, new ResourceDescriptor(resourceType, scope));

    private static InMemoryResourceDescriptorRegistry BuildRegistry()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new FixedEnvironment()).Seed(registry);
        return registry;
    }

    private sealed class FixedEnvironment : IEnvironmentContext
    {
        public string EnvironmentId => "local";
    }

    // ---- Arranging data (raw SQL, as the catalog could hold it) ----

    public async Task<string> NewRoleAsync(string stem, params PermissionDescriptor[] grants) =>
        await NewRawRoleAsync(stem, StoredRolePermissions.Serialize(grants));

    public async Task<string> NewRawRoleAsync(string stem, string permissionsJson, bool system = false)
    {
        var id = $"role-{stem}-{Guid.NewGuid().ToString("N")[..8]}";
        await using var connection = new SqlConnection(Scratch.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Roles (Id, Name, Description, PermissionsJson, IsSystemRole) VALUES (@id, @id, NULL, @json, @system);";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@json", permissionsJson);
        command.Parameters.AddWithValue("@system", system);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    public async Task<string> NewUserAsync(string stem, params string[] roleIds)
    {
        var id = NewUserId();
        await Scratch.AddUserAsync(id, NewUsername(stem));
        foreach (var roleId in roleIds)
            await AssignRawAsync(id, roleId);
        return id;
    }

    /// <summary>A person who can sign in to a test host by their <see cref="Person.Username"/>.</summary>
    public sealed record Person(string Id, string Username);

    public async Task<Person> NewPersonAsync(string stem, params string[] roleIds)
    {
        var id = NewUserId();
        var username = NewUsername(stem);
        await Scratch.AddUserAsync(id, username);
        foreach (var roleId in roleIds)
            await AssignRawAsync(id, roleId);
        return new Person(id, username);
    }

    public Task AssignRawAsync(string userId, string roleId) =>
        Scratch.ExecuteAsync($"INSERT INTO UserRoles (UserId, RoleId) VALUES (N'{userId}', N'{roleId}');");

    public Task<bool> AssignmentExistsAsync(string userId, string roleId) =>
        Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserRoles WHERE UserId = N'{userId}' AND RoleId = N'{roleId}';").ContinueWith(t => t.Result > 0);

    public Task<int> AssignmentCountAsync(string userId, string roleId) =>
        Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserRoles WHERE UserId = N'{userId}' AND RoleId = N'{roleId}';");

    public Task<string> RoleJsonAsync(string roleId) =>
        Scratch.ScalarAsync<string>($"SELECT PermissionsJson FROM Roles WHERE Id = N'{roleId}';");

    public async Task<IReadOnlyList<PermissionDescriptor>> RoleGrantsAsync(string roleId) =>
        StoredRolePermissions.Read(await RoleJsonAsync(roleId)).Permissions;

    public Task<bool> RoleExistsAsync(string roleId) =>
        Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM Roles WHERE Id = N'{roleId}';").ContinueWith(t => t.Result > 0);

    /// <summary>Audit rows of an action whose target is <paramref name="targetId"/>.</summary>
    public Task<int> AuditRowsAsync(string action, string targetId) =>
        Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{action}' AND TargetId = N'{targetId}';");

    public Task<List<Dictionary<string, object?>>> AuditRowDetailsAsync(string action, string targetId) =>
        Scratch.RowsAsync($"SELECT Action, TargetId, Outcome, Details, OperationEventId FROM AuditLogs WHERE Action = N'{action}' AND TargetId = N'{targetId}' ORDER BY Id;");

    // ---- The service under test ----

    /// <summary>
    /// The production user administration service over <paramref name="db"/>, acting as <paramref name="actorUserId"/>. The check
    /// made before the unit is the real evaluator unless <paramref name="advisoryAllowsAll"/>: the race cases switch it off so the
    /// decision under the lock is the only thing that can refuse, which is what they exist to show.
    /// </summary>
    public UserManagementService Service(
        AppertureDbContext db,
        string actorUserId,
        bool advisoryAllowsAll = false,
        IAuditedUnitOfWork? unit = null,
        IAuditEventWriter? events = null)
    {
        var principal = new Principal(actorUserId);
        var roles = new SqlRoleRepository(db);
        IAuthorizationService advisory = advisoryAllowsAll
            ? new AllowAllAuthorization()
            : new DelegatingAuthorizationService(
                new RbacAuthorizationPolicyEvaluator(new IdentityResolver(), new SqlRoleProvider(roles), new ScopeContainmentEvaluator()));
        return new UserManagementService(
            principal,
            new IdentityResolver(),
            advisory,
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            roles,
            Registry,
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            Options.Create(new LocalAuthOptions()),
            auditedUnitOfWork: unit ?? NewUnit(db, principal),
            auditEvents: events ?? NewWriter(principal));
    }

    public SqlAuditEventWriter NewWriter(IPrincipalContext principal) =>
        new(Scratch.Options(), principal, HarnessCatalog, null, null, null);

    private sealed class IdentityResolver : IPrincipalToUserResolver
    {
        public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<UserId?>(new UserId(actor.Id.Value));
    }

    private sealed class AllowAllAuthorization : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    /// <summary>
    /// A context with no retrying execution strategy, so a test of the repository alone can open its own transaction (EF allows a
    /// user transaction only without one, or inside it). The audited unit opens its transaction inside the strategy, as production does.
    /// </summary>
    public AppertureDbContext NewPlainDb(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(Scratch.ConnectionString)
            .AddInterceptors([AuditRecorderRowGuardInterceptor.Instance, .. interceptors])
            .Options);

    // ---- Waiting on the server's own lock list ----

    /// <summary>
    /// Waits until the server lists a request waiting for a lock on a row of <paramref name="table"/>. Fails if <paramref name="task"/>
    /// finishes first: an operation that did not wait did not take (or did not respect) the lock.
    /// </summary>
    public Task WaitUntilBlockedOnAsync(string table, Task task) => WaitUntilBlockedOnAnyAsync(task, table);

    /// <summary>As <see cref="WaitUntilBlockedOnAsync(string, Task)"/>, for a wait on a row of any of the given tables.</summary>
    public async Task WaitUntilBlockedOnAnyAsync(Task task, params string[] tables)
    {
        var table = string.Join("' OR OBJECT_NAME(p.object_id) = N'", tables);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (task.IsCompleted)
            {
                var detail = task.IsFaulted ? task.Exception!.GetBaseException().Message : "it completed";
                throw new Xunit.Sdk.XunitException($"The operation did not wait for a {table} row ({detail}).");
            }

            var waiting = await Scratch.ScalarAsync<int>(
                $"""
                SELECT COUNT(*)
                FROM sys.dm_tran_locks AS l
                INNER JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id
                WHERE l.request_status = N'WAIT'
                  AND l.resource_database_id = DB_ID()
                  AND l.resource_type IN (N'KEY', N'RID', N'PAGE')
                  AND (OBJECT_NAME(p.object_id) = N'{table}');
                """);
            if (waiting > 0)
                return;
            await Task.Delay(25);
        }

        // Say what the server did see, so a missing wait can be told from a wait somewhere else.
        var requests = await Scratch.RowsAsync(
            """
            SELECT r.session_id, r.status, r.wait_type, r.wait_resource, r.blocking_session_id, LEFT(t.text, 160) AS sql_text
            FROM sys.dm_exec_requests AS r
            CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) AS t
            WHERE r.database_id = DB_ID() AND r.session_id <> @@SPID;
            """);
        var seen = string.Join("; ", requests.Select(r => string.Join(", ", r.Select(kv => $"{kv.Key}={kv.Value}"))));
        var sessions = await Scratch.RowsAsync(
            """
            SELECT s.session_id, s.status, s.open_transaction_count, s.last_request_start_time, s.last_request_end_time
            FROM sys.dm_exec_sessions AS s
            WHERE s.is_user_process = 1 AND s.database_id = DB_ID() AND s.session_id <> @@SPID;
            """);
        var sessionText = string.Join("; ", sessions.Select(r => string.Join(", ", r.Select(kv => $"{kv.Key}={kv.Value}"))));
        throw new Xunit.Sdk.XunitException(
            $"No request was seen waiting on a {table} row within 30 seconds. Active requests: [{seen}]. Sessions: [{sessionText}]. Task: {task.Status}");
    }

    // ---- Interceptors and sessions ----

    /// <summary>
    /// Holds the first statement that satisfies <c>matches</c>, before it runs or just after it ran, until released. Whatever the
    /// transaction took up to that point stays held: the operation is parked with its locks while the test arranges the other side.
    /// Only the first matching statement of the context pauses, so a rerun after a deadlock victim is not held again.
    /// </summary>
    public sealed class PauseAt : DbCommandInterceptor
    {
        // Every pause ever made, so a test that fails before it releases one cannot leave an operation parked, holding locks, for
        // the next test: the test class releases them all when it is done.
        private static readonly System.Collections.Concurrent.ConcurrentBag<PauseAt> Made = [];

        private readonly Func<DbCommand, bool> matches;
        private readonly bool after;
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _paused;

        public PauseAt(Func<DbCommand, bool> matches, bool after)
        {
            this.matches = matches;
            this.after = after;
            Made.Add(this);
        }

        public Task Reached => _reached.Task;

        public void Release() => _release.TrySetResult();

        /// <summary>Lets every parked operation go.</summary>
        public static void ReleaseAll()
        {
            foreach (var pause in Made)
                pause.Release();
        }

        /// <summary>Before the first statement whose text contains <paramref name="text"/> runs.</summary>
        public static PauseAt BeforeStatement(string text) =>
            new(c => c.CommandText.Contains(text, StringComparison.Ordinal), after: false);

        /// <summary>After the locked read of the Roles row <paramref name="roleId"/>: the last lock of an operation whose target sorts last.</summary>
        public static PauseAt AfterRoleLock(string roleId) =>
            new(c => IsRoleRowLock(c.CommandText) && c.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, roleId)), after: true);

        /// <summary>After the locked read of the target assignment key: the last lock of an assignment or removal.</summary>
        public static PauseAt AfterAssignmentKeyLock() =>
            new(c => c.CommandText.Contains("SELECT * FROM [UserRoles]", StringComparison.Ordinal)
                     && c.CommandText.Contains("AND [RoleId]", StringComparison.Ordinal), after: true);

        /// <summary>After the locked read of the actor's assignments: the first lock, before any role row is taken.</summary>
        public static PauseAt AfterActorAssignmentLock() => new(c => IsActorAssignmentLock(c.CommandText), after: true);

        private async Task PauseIfAsync(DbCommand command, bool isAfter, CancellationToken cancellationToken)
        {
            if (isAfter != after || !matches(command) || Interlocked.Exchange(ref _paused, 1) != 0)
                return;
            _reached.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(90), cancellationToken);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await PauseIfAsync(command, isAfter: false, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await PauseIfAsync(command, isAfter: false, cancellationToken);
            return result;
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            await PauseIfAsync(command, isAfter: true, cancellationToken);
            return result;
        }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            await PauseIfAsync(command, isAfter: true, cancellationToken);
            return result;
        }
    }

    /// <summary>Counts the statements that satisfy <c>matches</c>.</summary>
    public sealed class StatementCounter(Func<string, bool> matches) : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (matches(command.CommandText))
                Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }
    }

    public static bool IsActorAssignmentLock(string sql) =>
        sql.Contains("SELECT * FROM [UserRoles]", StringComparison.Ordinal)
        && !sql.Contains("AND [RoleId]", StringComparison.Ordinal);

    public static bool IsRoleRowLock(string sql) => sql.Contains("SELECT * FROM [Roles]", StringComparison.Ordinal);

    /// <summary>A second session that holds an open transaction and runs whatever it is told: another writer of the same rows.</summary>
    public sealed class RawSession : IAsyncDisposable
    {
        private readonly SqlConnection _connection;
        private SqlTransaction? _transaction;

        private RawSession(SqlConnection connection, SqlTransaction transaction, int sessionId)
        {
            _connection = connection;
            _transaction = transaction;
            SessionId = sessionId;
        }

        public int SessionId { get; }

        public static async Task<RawSession> BeginAsync(string connectionString, bool highDeadlockPriority = false)
        {
            var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            if (highDeadlockPriority)
            {
                await using var priority = connection.CreateCommand();
                priority.CommandText = "SET DEADLOCK_PRIORITY HIGH;";
                await priority.ExecuteNonQueryAsync();
            }

            var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await using var spid = connection.CreateCommand();
            spid.Transaction = transaction;
            spid.CommandText = "SELECT CAST(@@SPID AS int);";
            return new RawSession(connection, transaction, (int)(await spid.ExecuteScalarAsync())!);
        }

        public async Task<int> ExecuteAsync(string sql, params (string Name, string Value)[] parameters)
        {
            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            return await command.ExecuteNonQueryAsync();
        }

        public async Task CommitAsync()
        {
            await _transaction!.CommitAsync();
            _transaction = null;
        }

        /// <summary>Ends the transaction without committing: a failed statement leaves it open, still holding its locks.</summary>
        public async Task RollbackAsync()
        {
            if (_transaction is null)
                return;
            await _transaction.RollbackAsync();
            _transaction = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (_transaction is not null)
            {
                try
                {
                    await _transaction.RollbackAsync();
                }
                catch
                {
                    // The connection is closed next either way.
                }

                _transaction = null;
            }

            await _connection.DisposeAsync();
        }
    }

    /// <summary>The production retry with a note of each deadlock victim it retries.</summary>
    public sealed class DeadlockCountingStrategy(Microsoft.EntityFrameworkCore.Storage.ExecutionStrategyDependencies dependencies, DeadlockLog log)
        : SqlServerRetryingExecutionStrategy(dependencies)
    {
        protected override bool ShouldRetryOn(Exception exception)
        {
            for (var inner = exception; inner is not null; inner = inner.InnerException)
            {
                if (inner is SqlException { Number: 1205 })
                {
                    log.Victims++;
                    break;
                }
            }

            return base.ShouldRetryOn(exception);
        }
    }

    public sealed class DeadlockLog
    {
        public int Victims;
    }

    public AppertureDbContext NewDeadlockAwareDb(DeadlockLog log, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(Scratch.ConnectionString, sql => sql.ExecutionStrategy(d => new DeadlockCountingStrategy(d, log)))
            .AddInterceptors([AuditRecorderRowGuardInterceptor.Instance, .. interceptors])
            .Options);
}
