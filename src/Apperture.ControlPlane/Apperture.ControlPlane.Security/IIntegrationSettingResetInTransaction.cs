using Apperture.Core.Application.Audit;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Resets one integration-scoped setting inside an audited unit that is already open, for an operation that removes settings
/// as part of a larger change (AUD-1.4 slice 2). A unit does not nest, so the public
/// <c>IIntegrationSettingsService.ResetAsync</c>, which opens its own, cannot be called from inside one.
/// <b>Currently unused by production code, and unsuitable for historical-override cleanup:</b> see the last paragraph below.
/// </summary>
/// <remarks>
/// <para><b>Contract.</b> The caller holds the open unit's <see cref="IAuditedTransaction"/> and has already authorized the
/// larger change; the helper authorizes nothing, as <c>ResetAsync</c> does not. It is <c>internal</c> to the services assembly and
/// registered only as this interface, so no controller or other assembly can reach it and it is not a second way to reset a
/// setting from a request.</para>
/// <para>It reads and writes through the scoped context the unit uses, so the setting row, the secret it disables and the
/// store's evidence join the unit's transaction. It never begins, commits or rolls back a transaction, never opens a unit and
/// never saves anything outside that transaction: it stages its audit rows first, and staging fails before any change when the
/// transaction passed in is not the current one.</para>
/// <para>For a setting that has an override it removes the override, disables the secret the setting owns (a sensitive setting),
/// and stages, as secondary rows of the caller's unit, the setting's reset row and (when a secret was disabled) its Secrets-category
/// retirement row. The caller's own primary row identifies the operation. When there is no override it changes and stages
/// nothing and returns false, since the caller's unit may already hold staged rows and cannot be declared a no-op here.</para>
/// <para>A setting removed by another request between the helper's read and its delete is a concurrency conflict
/// (<c>DbUpdateConcurrencyException</c>) that rolls the caller's unit back; it is never reported as a reset this request made.</para>
/// <para>The caller's lock and ownership gate are untouched: the helper takes no lock of its own.</para>
/// <para><b>Not used by the integration delete.</b> It was written for it, but this helper disables the secret a sensitive setting's
/// name matches without looking for references that survive the delete, and ignores keys with no definition, which owner ruling
/// H1 forbids. The integration delete (work package 4b) removes its overrides by row and takes its secret decisions from
/// <c>IntegrationSettingSecretClassifier</c> and <c>IntegrationSettingSecretRetirement</c>. No production caller remains; it is kept,
/// with the slice 2 tests that pin its transactional behaviour, because removing it would touch the settings service and its
/// fixtures for no gain, and it is not a way to clean up an integration's historical overrides. A new caller must not use it for
/// a setting whose secret an H1 plan governs, nor to remove an override whose key may no longer have a definition.</para>
/// </remarks>
internal interface IIntegrationSettingResetInTransaction
{
    /// <summary>True when an override existed and was removed in <paramref name="transaction"/>; false when there was none.</summary>
    /// <exception cref="ArgumentException">The key is not an integration-scoped setting.</exception>
    Task<bool> ResetInTransactionAsync(
        IAuditedTransaction transaction,
        string integrationId,
        string key,
        CancellationToken cancellationToken = default);
}
