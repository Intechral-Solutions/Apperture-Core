using System.Data.Common;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AuditEvent = Apperture.Core.Application.Audit.AuditEvent;

namespace Apperture.IntegrationTests.Audit;

/// <summary>Fault injection and wiring shared by the AUD-1.1 recorder suites.</summary>
internal static class AuditRecorderTestSupport
{
    public static string NewUserId() => Guid.NewGuid().ToString("N")[..24];

    public static string NewUsername(string stem) => $"{stem}.{Guid.NewGuid().ToString("N")[..8]}x";

    /// <summary>Test catalog: the production actions plus harness actions for multi-row and delete operations.</summary>
    public static AuditActionCatalog HarnessCatalog { get; } = CreateHarnessCatalog();

    public static AuditActionDefinition HarnessPrimary => HarnessCatalog.Get("harness.item.changed");
    public static AuditActionDefinition HarnessSecondary => HarnessCatalog.Get("harness.item.related");
    public static AuditActionDefinition HarnessDeleted => HarnessCatalog.Get("harness.item.deleted");
    public static AuditActionDefinition HarnessAttempt => HarnessCatalog.Get("harness.item.refused");
    public static AuditActionDefinition HarnessTelemetry => HarnessCatalog.Get("harness.item.observed");

    public static AuditEvent HarnessEvent(AuditActionDefinition definition, string itemId, string? note = null) =>
        AuditEvent.Create(
            definition,
            null,
            definition.WriteClass == AuditWriteClass.Attempt ? AuditOutcomeCodes.Rejected : AuditOutcomeCodes.Succeeded,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["itemId"] = itemId, ["itemName"] = note });

    private static AuditActionCatalog CreateHarnessCatalog()
    {
        var schema = new AuditDetailSchema(
        [
            new AuditFieldDefinition("itemId", AuditFieldKind.Id, Required: true),
            new AuditFieldDefinition("itemName", AuditFieldKind.NameSnapshot, Required: false, MaxLength: 256, TruncatedLength: 32),
        ]);
        AuditActionDefinition Harness(string action, AuditWriteClass writeClass, string basis) => new()
        {
            Action = action,
            Category = AuditCategories.Configuration,
            Severity = AuditSeverities.Standard,
            Basis = basis,
            WriteClass = writeClass,
            Details = schema,
            IntroducedIn = "test",
            IntroducedAfterEpic37Migration = true,
        };

        return AuditActionCatalog.Default.With(
            Harness("harness.item.changed", AuditWriteClass.RequiredMutation, AuditBases.Change),
            Harness("harness.item.related", AuditWriteClass.RequiredMutation, AuditBases.Change),
            Harness("harness.item.deleted", AuditWriteClass.RequiredMutation, AuditBases.Change),
            Harness("harness.item.refused", AuditWriteClass.Attempt, AuditBases.Attempt),
            Harness("harness.item.observed", AuditWriteClass.Telemetry, AuditBases.Telemetry));
    }

    public static SqlAuditedUnitOfWork NewUnit(
        AppertureDbContext db,
        IPrincipalContext? principal,
        AuditRecorderOptions? options = null,
        IAuditEventIdSource? eventIds = null,
        IAuditVerificationConnectionFactory? verification = null) =>
        new(db, principal, HarnessCatalog, TimeProvider.System, options ?? FastOptions(), NullLogger<SqlAuditedUnitOfWork>.Instance, eventIds, verification);

    public static AuditRecorderOptions FastOptions() => new()
    {
        OperationLockWait = TimeSpan.FromSeconds(2),
        VerificationLockWait = TimeSpan.FromMilliseconds(750),
    };

    /// <summary>The production user administration service on a SQL context, with the given unit of work.</summary>
    public static UserManagementService RenameService(
        AppertureDbContext db,
        IPrincipalContext principal,
        IAuditedUnitOfWork unit,
        IAuditEventWriter? events = null,
        IEmergencyAccessService? emergencyAccess = null,
        IResourceDescriptorRegistry? registry = null,
        IAccountInvitationService? invitations = null,
        IAccountActionTokenService? actionTokens = null) =>
        new(
            principal,
            new NoResolver(),
            new AllowAll(),
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlRoleRepository(db),
            registry ?? new InMemoryResourceDescriptorRegistry(),
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            Options.Create(new LocalAuthOptions()),
            emergencyAccess: emergencyAccess,
            invitations: invitations,
            actionTokens: actionTokens,
            auditedUnitOfWork: unit,
            auditEvents: events);

    /// <summary>A signed-in person. The display name stands for a session claim the recorder must never trust.</summary>
    public sealed class Principal(string userId, string staleSessionName = "stale.session.name") : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new HumanActor(new ActorId(userId), staleSessionName, IdentitySource.Local));
    }

    private sealed class NoResolver : IPrincipalToUserResolver
    {
        public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<UserId?>(new UserId(actor.Id.Value));
    }

    private sealed class AllowAll : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    /// <summary>Returns the given ids in order, then fresh ones.</summary>
    public sealed class FixedEventIds(params string[] ids) : IAuditEventIdSource
    {
        private readonly Queue<string> _ids = new(ids);

        public string NewEventId() => _ids.Count > 0 ? _ids.Dequeue() : Guid.NewGuid().ToString("N");
    }

    /// <summary>Verification cannot reach the database.</summary>
    public sealed class UnreachableVerification : IAuditVerificationConnectionFactory
    {
        public int Calls { get; private set; }

        public Task<SqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Injected: the verification connection could not be opened.");
        }
    }

    /// <summary>
    /// The commit reaches the server and succeeds, then the acknowledgement is lost: the client sees an exception.
    /// Optionally runs <see cref="AfterCommit"/> first (for example to take the operation lock from another session).
    /// </summary>
    public sealed class LostCommitAcknowledgement : DbTransactionInterceptor
    {
        public int Remaining { get; set; } = 1;
        public int Commits { get; private set; }
        public Func<Task>? AfterCommit { get; set; }

        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Commits++;
            if (Remaining <= 0)
                return;
            Remaining--;
            if (AfterCommit is not null)
                await AfterCommit();
            throw new InvalidOperationException("Injected: the connection dropped after COMMIT was sent; no acknowledgement arrived.");
        }
    }

    /// <summary>The commit never happens: the server rolls the transaction back and the client sees an exception.</summary>
    public sealed class RolledBackCommit : DbTransactionInterceptor
    {
        public int Remaining { get; set; } = 1;

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Remaining <= 0)
                return result;
            Remaining--;
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("Injected: the commit failed and the server rolled the transaction back.");
        }
    }

    /// <summary>Counts transactions started on the context: one per attempt of a unit of work.</summary>
    public sealed class AttemptCounter : DbTransactionInterceptor
    {
        public int Started { get; private set; }

        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            Started++;
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Fails any command whose text matches, as an audit insert or a lookup failing at the database would.</summary>
    public sealed class FailingCommand(Func<string, bool> matches) : DbCommandInterceptor
    {
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return ValueTask.FromResult(result);
        }

        private void Check(DbCommand command)
        {
            if (!matches(command.CommandText))
                return;
            Failures++;
            throw new InvalidOperationException("Injected: the database refused this statement.");
        }
    }

    /// <summary>Holds an application lock from another session until disposed.</summary>
    public sealed class HeldLock : IAsyncDisposable
    {
        private readonly SqlConnection _connection;

        private HeldLock(SqlConnection connection) => _connection = connection;

        public static async Task<HeldLock> TakeAsync(string connectionString, string resource)
        {
            var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "EXEC sp_getapplock @Resource = @r, @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 5000;";
            command.Parameters.AddWithValue("@r", resource);
            await command.ExecuteNonQueryAsync();
            return new HeldLock(connection);
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }
}
