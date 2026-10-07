using Apperture.Core.Application.Audit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// The recorder as a service under test sees it: an <see cref="IAuditedUnitOfWork"/> and an <see cref="IAuditEventWriter"/>
/// in one, keeping every committed row in order. A W1 operation's rows count only when it completed (not a no-op, no
/// exception); a W2 or W3 row counts when it was written. The transactional guarantees themselves are proven against SQL
/// Server; this double proves which rows a service decides to record.
/// </summary>
internal sealed class AuditRecording : IAuditedUnitOfWork, IAuditEventWriter
{
    public RecordingAuditedUnitOfWork Unit { get; } = new();
    public RecordingAuditEventWriter Writer { get; } = new();

    /// <summary>Committed actions, in order. Tests may clear it between steps.</summary>
    public List<string> Actions { get; } = [];

    /// <summary>Committed events, in order, with the actor each was recorded under.</summary>
    public List<(AuditEvent Event, AuditActor Actor)> Events { get; } = [];

    public AuditEvent Last => Events[^1].Event;

    public AuditEvent Single(string action) => Events.Single(e => e.Event.Action == action).Event;

    /// <summary>When set, W2 writes throw and W3 writes report failure, as a database failure would.</summary>
    public bool FailWrites
    {
        get => Writer.Fail;
        set => Writer.Fail = value;
    }

    public void Clear()
    {
        Actions.Clear();
        Events.Clear();
    }

    public async Task<T> ExecuteAsync<T>(
        AuditedOperation operation,
        Func<IAuditedTransaction, CancellationToken, Task<T>> attempt,
        Func<AuditCommittedOperation, CancellationToken, Task<T>> reconstruct,
        CancellationToken cancellationToken = default)
    {
        var index = Unit.Operations.Count;
        var result = await Unit.ExecuteAsync(operation, attempt, reconstruct, cancellationToken);
        var recorded = Unit.Operations[index];
        if (recorded.Completed)
        {
            foreach (var staged in recorded.Staged)
                Add(staged, operation.Actor);
        }

        return result;
    }

    public async Task WriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default)
    {
        await Writer.WriteAsync(auditEvent, actor, cancellationToken);
        Add(auditEvent, actor);
    }

    public async Task<bool> TryWriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default)
    {
        var written = await Writer.TryWriteAsync(auditEvent, actor, cancellationToken);
        if (written)
            Add(auditEvent, actor);
        return written;
    }

    private void Add(AuditEvent auditEvent, AuditActor actor)
    {
        Actions.Add(auditEvent.Action);
        Events.Add((auditEvent, actor));
    }
}
