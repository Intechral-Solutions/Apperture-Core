using Apperture.Core.Application.Audit;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// A refusal an attempt decided inside its transaction: the W3 event to record and the exception to raise once the
/// transaction has rolled back (AUD-1 design gate §13.1).
/// </summary>
internal sealed record AuditRefusal(AuditEvent Event, Exception Exception);

/// <summary>
/// The refusal slot of one audited operation. An attempt that decides to refuse records it here and changes nothing; the
/// slot is cleared at the start of every attempt, so only the attempt whose outcome stands can leave a refusal.
/// </summary>
internal sealed class AuditRefusalSlot
{
    public AuditRefusal? Current { get; private set; }

    /// <summary>Records the refusal and declares the attempt a no-op: nothing it did may commit.</summary>
    public T Refuse<T>(IAuditedTransaction transaction, AuditEvent refusalEvent, Exception exception)
    {
        Current = new AuditRefusal(refusalEvent, exception);
        transaction.DeclareNoOp();
        return default!;
    }

    internal void Reset() => Current = null;
}

/// <summary>
/// Runs a W1 operation whose safety and validity checks may refuse it. The checks run inside the operation's transaction,
/// against the state the change would commit against. A refusal changes nothing; its W3 row is written only after the
/// transaction has rolled back and released its locks, on a context of its own, and a failure to write it is logged without
/// changing the refusal the caller sees (§13.1).
/// </summary>
internal static class AuditedOperations
{
    public static async Task<T> ExecuteRefusableAsync<T>(
        this IAuditedUnitOfWork unit,
        IAuditEventWriter events,
        AuditedOperation operation,
        Func<IAuditedTransaction, AuditRefusalSlot, CancellationToken, Task<T>> attempt,
        Func<AuditCommittedOperation, CancellationToken, Task<T>> reconstruct,
        CancellationToken cancellationToken)
    {
        var slot = new AuditRefusalSlot();
        var result = await unit.ExecuteAsync(
            operation,
            (transaction, ct) =>
            {
                slot.Reset();
                return attempt(transaction, slot, ct);
            },
            reconstruct,
            cancellationToken).ConfigureAwait(false);

        if (slot.Current is not { } refusal)
            return result;

        await events.TryWriteAsync(refusal.Event, operation.Actor, CancellationToken.None).ConfigureAwait(false);
        throw refusal.Exception;
    }
}
