using Apperture.Core.Application.Audit;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// The execution phase of an integration delete's secret disposition (AUD-1.4 slice 3, work package 4a): retires the secrets an
/// <see cref="IntegrationSettingDeletionPlan"/> classified as safe, inside the caller's audited unit.
/// </summary>
/// <remarks>
/// <para><b>Transaction.</b> It commits nothing and opens nothing. The secret store joins the transaction the caller's unit holds on
/// the shared context (AUD-1.4 slice 1), so each secret's disabling, the store's own evidence and the retirement row commit or roll
/// back with the caller's delete. A caller that refuses or fails after this runs rolls the retirements back with everything else.</para>
/// <para><b>Order.</b> The caller classifies first, as a read, and acts only on a plan that is not refused: a plan that is refused
/// carries no retirement, and <see cref="RetireAsync"/> throws before touching anything if given one. The retirements run after the
/// caller removed the overrides, so a secret is never retired while the row that named it remains.</para>
/// <para><b>Each secret is retired at most once.</b> The plan holds a secret once however many references named it; this repeats
/// that guard, and re-reads the secret in the caller's transaction so a secret that is no longer usable (disabled in the meantime) is
/// skipped without a row, as it would not be a retirement this delete made. It is staged before it is changed, so a transaction that
/// is not current fails before anything is retired.</para>
/// <para><b>Evidence.</b> One Secrets-category row per retirement, a secondary row of the caller's unit, targeting the integration
/// setting the secret belonged to with the reason <c>integration_deleted</c>. It carries neither the secret nor its id. A failure to
/// retire is reported without the store's own message, which embeds the secret's id.</para>
/// <para><b>Not the in-transaction settings reset.</b> <c>IIntegrationSettingResetInTransaction</c> disables whatever secret the
/// setting's name matches without looking for surviving references. An integration delete must not use it for a sensitive setting
/// and must take its secret decisions from the plan.</para>
/// </remarks>
public sealed class IntegrationSettingSecretRetirement
{
    private readonly ISecretStore _secretStore;

    public IntegrationSettingSecretRetirement(ISecretStore secretStore) =>
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));

    /// <summary>
    /// Retires every secret the plan classified as safe, staging a row for each in <paramref name="transaction"/>.
    /// </summary>
    /// <param name="resourceContextId">The integration's owning resource context, recorded as the rows' related context.</param>
    /// <returns>The number of secrets retired.</returns>
    /// <exception cref="IntegrationSettingDispositionRefusedException">The plan is refused; nothing was retired.</exception>
    /// <exception cref="IntegrationSettingSecretRetirementException">The store failed to retire a secret.</exception>
    public async Task<int> RetireAsync(
        IAuditedTransaction transaction,
        IntegrationSettingDeletionPlan plan,
        string? resourceContextId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(plan);
        plan.ThrowIfRefused();

        var retired = 0;
        var seen = new HashSet<Guid>();
        foreach (var planned in plan.Retirements)
        {
            if (!seen.Add(planned.SecretId))
                continue;

            var secretId = new SecretId(planned.SecretId);
            var descriptor = await _secretStore.GetAsync(secretId, cancellationToken).ConfigureAwait(false);
            if (descriptor is null
                || !planned.Owner.Matches(descriptor.Provider, descriptor.Name)
                || descriptor.State is SecretState.Disabled or SecretState.Revoked)
            {
                continue;
            }

            await transaction.StageAsync(
                AuditEvents.IntegrationSettingSecretRetired(
                    plan.IntegrationId, planned.SettingKey, AuditEvents.IntegrationDeletedSecretRetirementReason, resourceContextId),
                cancellationToken).ConfigureAwait(false);

            try
            {
                await _secretStore.DisableAsync(secretId, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (Mentions(ex, planned.SecretId))
            {
                // The store's message embeds the secret's id; only the setting and the failure's type are kept. A failure whose
                // text does not name the secret (a staged row that could not be saved, say) propagates unchanged.
                throw new IntegrationSettingSecretRetirementException(planned.SettingKey, ex.GetType().Name);
            }

            retired++;
        }

        return retired;
    }

    private static bool Mentions(Exception exception, Guid secretId)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e.Message.Contains(secretId.ToString("D"), StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains(secretId.ToString("N"), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
