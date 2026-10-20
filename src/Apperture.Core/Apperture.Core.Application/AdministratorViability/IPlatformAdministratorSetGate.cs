namespace Apperture.Core.Application.AdministratorViability;

/// <summary>
/// The one global gate that serializes every writer able to change administrator viability or recovery continuity
/// (design gate IAM-10 §3.2, P-8). It is a transaction-owned SQL Server application lock on
/// <see cref="PlatformAdministratorSetLock.ResourceName"/>, taken inside the writer's own unit, so it is released by that unit's
/// commit or rollback. The per-actor and per-target row locks of the authority snapshot are not a substitute: two writers acting
/// on different actors and different targets hold disjoint row locks and together can eliminate the last viable administrator.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lock order.</b> The audited unit's operation lock, then this gate, then the authority row locks, then every other row.
/// A writer that holds only row locks never waits for the gate, and no writer requests the gate after taking a row lock, so no
/// cycle can form.
/// </para>
/// <para>
/// <b>Read discipline.</b> Every invariant read (the state before, the state after, the recovery posture) is issued after the
/// gate is held, so that under both locking READ COMMITTED and READ_COMMITTED_SNAPSHOT each statement observes everything the
/// previous holder committed. The gate must be acquired again on every attempt of a unit: a rerun after a deadlock or a confirmed
/// rollback is a new transaction, and the earlier hold was released with the earlier transaction.
/// </para>
/// <para>
/// <b>One name, one purpose.</b> This is not a lock framework. Nothing else calls <c>sp_getapplock</c> for this resource, and it
/// is never taken together with the resource ownership gate.
/// </para>
/// </remarks>
public interface IPlatformAdministratorSetGate
{
    /// <summary>Acquires the gate for the lifetime of the caller's ambient transaction.</summary>
    /// <exception cref="InvalidOperationException">No transaction is open on the underlying context (a programming error).</exception>
    /// <exception cref="PlatformAdministratorSetBusyException">
    /// Another writer held the gate past the bound, the request was cancelled by the server, or it was chosen as a deadlock
    /// victim. Transient and safe to retry.
    /// </exception>
    /// <exception cref="PlatformAdministratorSetGateException">The lock call failed for a reason that is not contention.</exception>
    ValueTask AcquireAsync(CancellationToken cancellationToken = default);
}

/// <summary>What <c>sp_getapplock</c> reported, as the gate reads it.</summary>
public enum PlatformAdministratorSetLockOutcome
{
    /// <summary>0: granted immediately.</summary>
    Granted,

    /// <summary>1: granted after waiting.</summary>
    GrantedAfterWaiting,

    /// <summary>-1: the bounded wait expired.</summary>
    TimedOut,

    /// <summary>-2: the request was cancelled.</summary>
    Cancelled,

    /// <summary>-3: the request was chosen as a deadlock victim.</summary>
    DeadlockVictim,

    /// <summary>-999, any other value, or no value: the call itself failed.</summary>
    Failed,
}

/// <summary>The gate's resource name, bound and the reading of the lock call's return code.</summary>
public static class PlatformAdministratorSetLock
{
    /// <summary>The one and only resource name. Never parameterize or extend it.</summary>
    public const string ResourceName = "apperture:platform-administrator-set";

    /// <summary>
    /// The bound on the wait, the ownership gate's value: long enough that an operator mutation queued behind another one never
    /// fails, short enough that a stuck transaction becomes a clear transient conflict instead of a hung request.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Reads a <c>sp_getapplock</c> return code. Pure, so every code is testable without producing it on a server.</summary>
    public static PlatformAdministratorSetLockOutcome Interpret(int? returnCode) => returnCode switch
    {
        0 => PlatformAdministratorSetLockOutcome.Granted,
        1 => PlatformAdministratorSetLockOutcome.GrantedAfterWaiting,
        -1 => PlatformAdministratorSetLockOutcome.TimedOut,
        -2 => PlatformAdministratorSetLockOutcome.Cancelled,
        -3 => PlatformAdministratorSetLockOutcome.DeadlockVictim,
        _ => PlatformAdministratorSetLockOutcome.Failed,
    };

    /// <summary>True for the two outcomes in which the caller holds the gate.</summary>
    public static bool IsHeld(PlatformAdministratorSetLockOutcome outcome) =>
        outcome is PlatformAdministratorSetLockOutcome.Granted or PlatformAdministratorSetLockOutcome.GrantedAfterWaiting;
}

/// <summary>
/// Another change to administrator access holds the gate. Transient contention (HTTP 409 with a retry hint), distinct from a
/// business refusal and from an audit-infrastructure failure. Its stable code is the one the governance failure mapping already
/// carries for this outcome.
/// </summary>
public sealed class PlatformAdministratorSetBusyException : Exception
{
    /// <summary>The stable machine code, the value of the governance busy outcome.</summary>
    public const string BusyCode = "governance_operation_busy";

    public PlatformAdministratorSetBusyException(PlatformAdministratorSetLockOutcome outcome)
        : base("Another change to administrator access is in progress. Try again in a moment.")
    {
        Outcome = outcome;
    }

    /// <summary>Which contention outcome the server reported.</summary>
    public PlatformAdministratorSetLockOutcome Outcome { get; }

    /// <summary>The stable code for the API and the command line.</summary>
    public string Code => BusyCode;
}

/// <summary>
/// The lock call failed for a reason that is not contention (an unexpected return code). Not a business outcome and not safe to
/// present as a retry: the host's exception handler reports it as an internal failure.
/// </summary>
public sealed class PlatformAdministratorSetGateException : Exception
{
    /// <summary>The stable machine code.</summary>
    public const string FailureCode = "administrator_set_gate_failed";

    public PlatformAdministratorSetGateException(int? returnCode)
        : base($"The administrator-set gate could not be acquired (lock call result {(returnCode?.ToString() ?? "none")}).")
    {
        ReturnCode = returnCode;
    }

    /// <summary>The return code, when the call produced one.</summary>
    public int? ReturnCode { get; }

    /// <summary>The stable code.</summary>
    public string Code => FailureCode;
}
