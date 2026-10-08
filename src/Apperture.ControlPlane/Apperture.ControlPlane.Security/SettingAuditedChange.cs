using Apperture.Core.Application.Audit;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Runs one setting change as an audited unit and reads the response, keeping the three facts the response can rest on apart
/// (AUD-1 design gate §13.5).
/// </summary>
/// <remarks>
/// <para><b>Proof of commit.</b> The unit's confirmed commit, or its verification of an uncertain one. Nothing the response read
/// returns changes that.</para>
/// <para><b>Reconstruction.</b> When verification had to confirm the commit, the response is built inside the unit's
/// <c>reconstruct</c> delegate, so a failed read surfaces as <see cref="AuditCommittedResultUnavailableException"/> and not as a
/// generic failure. A commit the unit acknowledged directly is treated the same way: the response read follows a change that is
/// known to be applied, so its failure is reported as the same outcome rather than as an error that invites a blind retry.</para>
/// <para><b>Current state.</b> The response is the setting as it resolves when it is read. A later writer's change between the
/// commit and the read appears in it, so it is not a snapshot of the state this operation produced. A change declared a no-op
/// committed nothing, so its read is an ordinary read.</para>
/// </remarks>
internal static class SettingAuditedChange
{
    /// <param name="attempt">The change, run inside the unit. Returns true when it changed and staged, false when it declared a no-op.</param>
    /// <param name="readCurrent">Reads the response: the setting's resolved state at the time of the read.</param>
    public static async Task<T> ExecuteAsync<T>(
        IAuditedUnitOfWork unit,
        AuditedOperation operation,
        Func<IAuditedTransaction, CancellationToken, Task<bool>> attempt,
        Func<CancellationToken, Task<T>> readCurrent,
        CancellationToken cancellationToken)
    {
        string? appliedOperationId = null;
        var outcome = await unit.ExecuteAsync(
            operation,
            async (transaction, ct) =>
            {
                if (await attempt(transaction, ct).ConfigureAwait(false))
                    appliedOperationId = transaction.OperationEventId;
                return new Outcome<T>(Read: false, Value: default!);
            },
            async (_, ct) => new Outcome<T>(Read: true, Value: await readCurrent(ct).ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);

        if (outcome.Read)
            return outcome.Value;

        if (appliedOperationId is null)
            return await readCurrent(cancellationToken).ConfigureAwait(false);

        try
        {
            return await readCurrent(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AuditCommittedResultUnavailableException(appliedOperationId, ex);
        }
    }

    private sealed record Outcome<T>(bool Read, T Value);
}
