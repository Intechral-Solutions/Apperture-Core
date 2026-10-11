using System.Text.Json;
using Apperture.Core.Application.Audit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// An in-memory <see cref="IAuditedUnitOfWork"/> for service tests: runs the attempt once and records what it staged. The
/// transactional guarantees themselves are proven against SQL Server (AuditRecorderSqlServerTests).
/// </summary>
internal sealed class RecordingAuditedUnitOfWork : IAuditedUnitOfWork
{
    public List<RecordedOperation> Operations { get; } = [];

    /// <summary>Every event staged by an operation that completed, in order.</summary>
    public IEnumerable<AuditEvent> Staged => Operations.Where(o => o.Completed).SelectMany(o => o.Staged);

    /// <summary>
    /// How the next changing operation's commit is acknowledged, as the SQL unit would report it after verification:
    /// <see cref="CommitAcknowledgement.Lost"/> returns <c>reconstruct</c>'s result from the committed primary row instead
    /// of the attempt's; <see cref="CommitAcknowledgement.Unknown"/> throws <see cref="AuditOutcomeUnknownException"/>.
    /// </summary>
    public CommitAcknowledgement Acknowledgement { get; set; } = CommitAcknowledgement.Received;

    /// <summary>
    /// The user the unit records for an operation opened with <see cref="AuditActor.CurrentPrincipal"/> (the real unit
    /// resolves it from the authenticated context). A named non-human entry is recorded as itself.
    /// </summary>
    public AuditActor CurrentPrincipalActor { get; set; } =
        AuditActor.AuthenticatedUser(new Apperture.Core.Domain.Identity.UserId("usr_unit_test_actor"));

    public async Task<T> ExecuteAsync<T>(
        AuditedOperation operation,
        Func<IAuditedTransaction, CancellationToken, Task<T>> attempt,
        Func<AuditCommittedOperation, CancellationToken, Task<T>> reconstruct,
        CancellationToken cancellationToken = default)
    {
        var recorded = new RecordedOperation(operation, operation.Actor.IsCurrentPrincipal ? CurrentPrincipalActor : operation.Actor);
        Operations.Add(recorded);
        var result = await attempt(recorded, cancellationToken);
        if (!recorded.NoOp && !recorded.Staged.Any(e => operation.AcceptsPrimary(e.Action)))
            throw new InvalidOperationException($"The operation expected a '{operation.PrimaryAction}' row and staged none.");
        if (recorded.NoOp)
            return result;

        switch (Acknowledgement)
        {
            case CommitAcknowledgement.Unknown:
                // Neither commit nor rollback established: the attempt's rows are not counted as committed.
                throw new AuditOutcomeUnknownException(recorded.OperationEventId);
            case CommitAcknowledgement.Lost:
                recorded.Completed = true;
                var primary = recorded.Staged.First(e => operation.AcceptsPrimary(e.Action));
                try
                {
                    return await reconstruct(
                        new AuditCommittedOperation(
                            recorded.OperationEventId,
                            primary.Action,
                            primary.Target?.Type,
                            primary.Target?.Id,
                            JsonSerializer.Serialize(primary.Fields)),
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // As the SQL unit: the change is committed, so a failed reconstruction is reported as such.
                    throw new AuditCommittedResultUnavailableException(recorded.OperationEventId, ex);
                }
            default:
                recorded.Completed = true;
                return result;
        }
    }

    internal enum CommitAcknowledgement
    {
        Received,
        Lost,
        Unknown,
    }

    /// <summary>The actions of every committed staged event, in order.</summary>
    public IReadOnlyList<string> Actions => Staged.Select(e => e.Action).ToList();

    /// <summary>The single committed event of <paramref name="action"/>.</summary>
    public AuditEvent Single(string action) => Staged.Single(e => e.Action == action);

    internal sealed class RecordedOperation(AuditedOperation operation, AuditActor actor) : IAuditedTransaction
    {
        public AuditedOperation Operation { get; } = operation;
        public AuditActor Actor { get; } = actor;
        public List<AuditEvent> Staged { get; } = [];
        public bool NoOp { get; private set; }
        public bool Completed { get; set; }
        public string OperationEventId { get; } = Guid.NewGuid().ToString("N");

        public ValueTask StageAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            if (NoOp)
                throw new InvalidOperationException("An attempt that declared it changed nothing cannot stage audit events.");
            Staged.Add(auditEvent);
            return ValueTask.CompletedTask;
        }

        public void DeclareNoOp()
        {
            if (Staged.Count > 0)
                throw new InvalidOperationException("An attempt that staged audit events cannot declare that it changed nothing.");
            NoOp = true;
        }
    }
}
