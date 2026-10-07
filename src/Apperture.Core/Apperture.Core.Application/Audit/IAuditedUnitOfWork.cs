using System.Text.Json;

namespace Apperture.Core.Application.Audit;

/// <summary>
/// Runs one W1 operation (§13.4, §13.5): one explicit transaction on the request's scoped context, opened inside the
/// execution strategy, whose first statement takes the operation lock. Every read and validation the decision depends on,
/// every mutation and every <see cref="IAuditedTransaction.StageAsync"/> call happen inside it, and the change and its
/// audit rows commit together.
/// </summary>
/// <remarks>
/// <para>The caller observes exactly one of: confirmed commit (the result), confirmed rollback (the attempt's own
/// exception, a genuine refusal or failure), or <see cref="AuditOutcomeUnknownException"/>. The platform never says
/// nothing changed unless it confirmed a rollback.</para>
/// <para>The operation event id is generated once, before the first attempt, and is the <c>EventId</c> of the operation's
/// primary row. When a commit call throws, the outcome is verified on a fresh connection under the operation lock before
/// anything reruns. A confirmed commit returns <paramref name="reconstruct"/>'s result built from the committed primary
/// row, without rerunning business logic. This is server-side handling within one operation; it is not HTTP idempotency.</para>
/// <para>External effects never run inside <c>attempt</c>: they follow the commit and are recorded separately.</para>
/// </remarks>
public interface IAuditedUnitOfWork
{
    Task<T> ExecuteAsync<T>(
        AuditedOperation operation,
        Func<IAuditedTransaction, CancellationToken, Task<T>> attempt,
        Func<AuditCommittedOperation, CancellationToken, Task<T>> reconstruct,
        CancellationToken cancellationToken = default);
}

/// <summary>The identity of one W1 operation.</summary>
/// <param name="PrimaryAction">The action whose row identifies the operation (<c>Expect(primaryAction)</c>).</param>
/// <param name="Actor">Who performs it: <see cref="AuditActor.CurrentPrincipal"/>, or a named non-human entry.</param>
/// <param name="AlternatePrimaryActions">
/// Other actions that may identify the operation, for an operation whose primary row depends on state it reads inside its
/// transaction (for example an invitation that is sent for the first time or resent). Exactly one primary row is staged,
/// and verification accepts any of the declared actions as the matching event (§13.5).
/// </param>
public sealed record AuditedOperation(
    string PrimaryAction,
    AuditActor Actor,
    IReadOnlyList<string>? AlternatePrimaryActions = null)
{
    /// <summary>True when <paramref name="action"/> may identify this operation.</summary>
    public bool AcceptsPrimary(string action) =>
        string.Equals(action, PrimaryAction, StringComparison.Ordinal)
        || AlternatePrimaryActions?.Contains(action, StringComparer.Ordinal) == true;
}

/// <summary>The open audited transaction of one attempt.</summary>
public interface IAuditedTransaction
{
    /// <summary>The operation event id: stable across every attempt and verification of the operation.</summary>
    string OperationEventId { get; }

    /// <summary>
    /// Adds the event's row, complete, to this transaction. Reads the actor's username in this transaction. Throws
    /// <see cref="InvalidOperationException"/> when no transaction of this unit is current, the action is not a W1 catalog
    /// action with a schema, the details overflow, or the event was already staged.
    /// </summary>
    ValueTask StageAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Declares that this attempt legitimately decided to change nothing (for example a rename to the current name). The
    /// unit then commits nothing and stages no row.
    /// </summary>
    void DeclareNoOp();
}

/// <summary>The committed primary row of an operation, as verification read it.</summary>
public sealed record AuditCommittedOperation(
    string OperationEventId,
    string Action,
    string? TargetType,
    string? TargetId,
    string? Details)
{
    /// <summary>A string field of the primary row's <c>json/1</c> details, or null.</summary>
    public string? GetField(string name)
    {
        if (string.IsNullOrEmpty(Details))
            return null;
        using var document = JsonDocument.Parse(Details);
        return document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}

/// <summary>
/// Neither a commit nor a rollback could be established (§13.2). Either the change and its audit rows both committed or
/// neither did, never one without the other. Callers reload state and never resubmit automatically; a resubmission is a
/// new operation.
/// </summary>
public sealed class AuditOutcomeUnknownException(string operationEventId, Exception? inner = null)
    : Exception("The outcome of the operation could not be confirmed. It may or may not have been applied.", inner)
{
    /// <summary>The stable error code surfaced to clients.</summary>
    public const string Code = "outcome_unknown";

    public string OperationEventId { get; } = operationEventId;
}

/// <summary>
/// The operation's commit failed and verification confirmed that nothing was committed, but it could not be completed
/// within its bounded reruns. Nothing changed.
/// </summary>
public sealed class AuditOperationNotCompletedException(string operationEventId, Exception? inner = null)
    : Exception("The operation could not be completed. Nothing was changed.", inner)
{
    public string OperationEventId { get; } = operationEventId;
}

/// <summary>
/// Writes single-row events on a context of their own (§13.4), so they never flush a caller's pending changes:
/// <see cref="WriteAsync"/> for W2 (the row commits before the method returns; a failure throws, so the result the row
/// describes is not delivered), and <see cref="TryWriteAsync"/> for W3 and W4 (attempts, refusals, sign-out and telemetry:
/// best effort, logged, never throws, the caller's response unchanged).
/// </summary>
public interface IAuditEventWriter
{
    /// <summary>Commits the row in its own short transaction, reusing its event id on retry.</summary>
    Task WriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default);

    /// <summary>As <see cref="WriteAsync"/>, but for W3 and W4 events: a failure is logged and reported as false.</summary>
    Task<bool> TryWriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default);
}
