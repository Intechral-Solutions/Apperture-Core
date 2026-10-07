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

    public async Task<T> ExecuteAsync<T>(
        AuditedOperation operation,
        Func<IAuditedTransaction, CancellationToken, Task<T>> attempt,
        Func<AuditCommittedOperation, CancellationToken, Task<T>> reconstruct,
        CancellationToken cancellationToken = default)
    {
        var recorded = new RecordedOperation(operation);
        Operations.Add(recorded);
        var result = await attempt(recorded, cancellationToken);
        if (!recorded.NoOp && recorded.Staged.All(e => e.Action != operation.PrimaryAction))
            throw new InvalidOperationException($"The operation expected a '{operation.PrimaryAction}' row and staged none.");
        recorded.Completed = true;
        return result;
    }

    internal sealed class RecordedOperation(AuditedOperation operation) : IAuditedTransaction
    {
        public AuditedOperation Operation { get; } = operation;
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
