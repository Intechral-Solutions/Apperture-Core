namespace Apperture.Core.Application.ResourceContexts;

/// <summary>
/// The single serialization point for ResourceContext ownership mutations (design gate §8.7, CTX-3B
/// product decision P1).
/// </summary>
/// <remarks>
/// <para>
/// Eleven paths participate, and every one of them acquires this gate as the first thing it does after
/// opening its transaction, before any ownership-sensitive read, any row lock, and any mutation:
/// Integration move, Connection move, standalone Schedule move, binding creation, Integration-attached
/// Schedule creation, and ResourceContext archive (the original six), plus five added by the CTX-3B
/// archive-concurrency integrity pass to close create/reactivate-vs-archive races the original six
/// didn't cover: <c>IntegrationManagementService.CreateIntegrationAsync</c>,
/// <c>IntegrationManagementService.UpdateIntegrationAsync</c>'s Retired-exit branch (Retired -&gt;
/// {Active, Inactive}), <c>ConnectionManagementService.CreateConnectionAsync</c>,
/// <c>SqlScheduleManagementService.EnableAsync</c>, and
/// <c>SqlScheduleManagementService.UpdateScheduleAsync</c>'s and <c>PatchScheduleAsync</c>'s enable branches (a patch only
/// when it carries <c>Enabled = true</c>). Ordinary resource creation
/// (Integration, Connection) is therefore now in scope too, not just moves, binding, and archive.
/// </para>
/// <para>
/// <b>Why one gate rather than ordered row locks.</b> Ownership validity is a property of a *pair* of
/// rows (a binding's Integration and its Connection), and the two move operations approach that pair from
/// opposite ends. Locking only the primary row leaves the two moves completely unserialized; locking both
/// ends in each path creates an ABBA deadlock between Integration move and Connection move, made worse by
/// multi-row counterparty sets whose membership is itself racy. One lock means one order, so deadlock
/// between these paths is impossible by construction and no ordering invariant has to be maintained by
/// reviewers.
/// </para>
/// <para>
/// <b>This is not a distributed-lock framework.</b> One lock name, one purpose, one method. Do not
/// generalize it, do not add a second resource name, and do not call <c>sp_getapplock</c> directly
/// anywhere else.
/// </para>
/// <para>
/// <b>Scope and cost.</b> The lock is database-scoped and intentionally serializes these eleven
/// low-frequency operator mutations instance-wide. No runtime path — scheduler claim, dispatch, run
/// writes, readiness evaluation — acquires it, so throughput is unaffected.
/// </para>
/// </remarks>
public interface IResourceOwnershipMutationGate
{
    /// <summary>
    /// Acquires the ownership mutation gate for the lifetime of the caller's ambient transaction.
    /// Released automatically on commit or rollback.
    /// </summary>
    /// <exception cref="InvalidOperationException">No transaction is open on the underlying context.</exception>
    /// <exception cref="ResourceOwnershipBusyException">
    /// Another ownership mutation held the gate past the configured timeout. Transient: the caller should
    /// surface it as a retryable conflict, never as an internal error.
    /// </exception>
    ValueTask AcquireAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Another ownership mutation is in progress and did not release the gate within the bounded timeout.
/// Transient and safe to retry. Carries a stable machine code so the API can map it to a specific
/// operator-facing message rather than a generic 500.
/// </summary>
public sealed class ResourceOwnershipBusyException : Exception
{
    /// <summary>Stable machine code for the API/UI. Plan 5 maps this to 409.</summary>
    public const string BusyCode = "ownership_operation_busy";

    public ResourceOwnershipBusyException(string message) : base(message) { }

    public string Code => BusyCode;
}
