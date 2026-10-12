using System.Text;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The M6 obligations of one audited W1 operation against SQL Server (design gate §13.2, §13.5; AUD-1.4 record §6): an audit
/// insert failure leaves nothing, a lost acknowledgement is verified and reconciled without running the attempt again, a
/// confirmed rollback reruns it once, an unknown outcome is wholly committed or wholly absent, and an operation lock that
/// cannot be taken is an unknown outcome that changes nothing. The operation, and what "its state" means, are the caller's.
/// </summary>
/// <param name="Scratch">The throwaway catalog the operation runs against.</param>
/// <param name="Snapshot">A stable text of every row the operation can change. It must differ before and after a commit.</param>
/// <param name="ExpectedAction">The primary action of the operation's one audit row.</param>
/// <param name="Operation">Runs the operation once on the given context and unit.</param>
/// <param name="Secret">A secret-looking value that must be absent from every audit column and every captured log line.</param>
internal sealed class AuditedOperationM6Runner(
    AuditSqlScratch Scratch,
    Func<Task<string>> Snapshot,
    string ExpectedAction,
    Func<AppertureDbContext, IAuditedUnitOfWork, Task> Operation,
    string? Secret = null)
{
    public async Task AuditFailureLeavesNothingAsync(IPrincipalContext principal)
    {
        var before = await Snapshot();
        var fail = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        var logs = new CapturingLogger();

        await using (var db = Scratch.NewDb(fail))
        {
            var act = () => Operation(db, Unit(db, principal, logs));
            // The injected failure itself, as the save reports it: a typed refusal or not-found is not this.
            (await act.Should().ThrowAsync<DbUpdateException>())
                .Which.InnerException.Should().BeOfType<InvalidOperationException>().Which.Message.Should().StartWith("Injected:");
            db.ChangeTracker.Entries().Should().BeEmpty("nothing the failed attempt tracked is left to save");
        }

        fail.Failures.Should().BeGreaterThan(0, "the audit row was attempted");
        (await Snapshot()).Should().Be(before, "no business change survives a failed audit write");
        (await AuditRowsAsync()).Should().BeEmpty();
        AssertNoSecret(logs);
    }

    public async Task LostAcknowledgementIsVerifiedAndReconciledAsync(IPrincipalContext principal)
    {
        var before = await Snapshot();
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();
        var logs = new CapturingLogger();

        await using (var db = Scratch.NewDb(lost, attempts))
        {
            var act = () => Operation(db, Unit(db, principal, logs));
            await act.Should().NotThrowAsync("the commit was verified, so the operation succeeded");
        }

        attempts.Started.Should().Be(1, "a verified commit is not run again");
        (await Snapshot()).Should().NotBe(before);
        (await AuditRowsAsync()).Should().ContainSingle(r => (string)r["Action"]! == ExpectedAction);
        AssertNoSecret(logs);
    }

    public async Task ConfirmedRollbackRerunsOnceAsync(IPrincipalContext principal)
    {
        var before = await Snapshot();
        var rolledBack = new RolledBackCommit();
        var attempts = new AttemptCounter();

        await using (var db = Scratch.NewDb(rolledBack, attempts))
            await Operation(db, Unit(db, principal, new CapturingLogger()));

        attempts.Started.Should().Be(2, "one attempt rolled back and the rerun committed");
        (await Snapshot()).Should().NotBe(before);
        (await AuditRowsAsync()).Should().ContainSingle(r => (string)r["Action"]! == ExpectedAction, "the rolled-back attempt left no row");
    }

    public async Task UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(IPrincipalContext principal, bool committed)
    {
        var before = await Snapshot();
        var fault = committed ? (Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor)new LostCommitAcknowledgement() : new RolledBackCommit();
        var logs = new CapturingLogger();

        await using (var db = Scratch.NewDb(fault))
        {
            var unit = new SqlAuditedUnitOfWork(db, principal, HarnessCatalog, TimeProvider.System, FastOptions(), logs, null, new UnreachableVerification());
            var act = () => Operation(db, unit);
            await act.Should().ThrowAsync<AuditOutcomeUnknownException>("verification could not establish the outcome");
        }

        var rows = await AuditRowsAsync();
        if (committed)
        {
            (await Snapshot()).Should().NotBe(before);
            rows.Should().ContainSingle(r => (string)r["Action"]! == ExpectedAction);
        }
        else
        {
            (await Snapshot()).Should().Be(before);
            rows.Should().BeEmpty();
        }

        AssertNoSecret(logs);
    }

    public async Task OperationLockNotGrantedIsUnknownAndChangesNothingAsync(IPrincipalContext principal)
    {
        var before = await Snapshot();
        var operationId = Guid.NewGuid().ToString("N");
        await using var held = await HeldLock.TakeAsync(Scratch.ConnectionString, "apperture.audit-op." + operationId);

        await using var db = Scratch.NewDb();
        var unit = NewUnit(db, principal, eventIds: new FixedEventIds(operationId));
        var act = () => Operation(db, unit);

        (await act.Should().ThrowAsync<AuditOutcomeUnknownException>()).Which.OperationEventId.Should().Be(operationId);
        (await Snapshot()).Should().Be(before);
        (await AuditRowsAsync()).Should().BeEmpty();
    }

    private SqlAuditedUnitOfWork Unit(AppertureDbContext db, IPrincipalContext principal, ILogger<SqlAuditedUnitOfWork> logger) =>
        new(db, principal, HarnessCatalog, TimeProvider.System, FastOptions(), logger, null, null);

    private Task<List<Dictionary<string, object?>>> AuditRowsAsync() => Scratch.RowsAsync("SELECT * FROM AuditLogs ORDER BY Id;");

    private void AssertNoSecret(CapturingLogger logs)
    {
        if (Secret is not null)
            logs.Text.Should().NotContain(Secret, "no captured log line may carry a secret-looking value");
    }

    /// <summary>Keeps every formatted log line and exception text of the unit.</summary>
    internal sealed class CapturingLogger : ILogger<SqlAuditedUnitOfWork>
    {
        private readonly StringBuilder _text = new();

        public string Text
        {
            get
            {
                lock (_text)
                    return _text.ToString();
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_text)
                _text.AppendLine(formatter(state, exception)).AppendLine(exception?.ToString());
        }
    }
}
