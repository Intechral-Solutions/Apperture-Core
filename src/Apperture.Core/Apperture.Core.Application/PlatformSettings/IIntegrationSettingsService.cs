namespace Apperture.Core.Application.PlatformSettings;

public interface IIntegrationSettingsService
{
    Task<IReadOnlyList<PlatformSettingResolvedValue>> ListAsync(
        string integrationId,
        CancellationToken cancellationToken = default);

    Task<PlatformSettingResolvedValue?> GetAsync(
        string integrationId,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets, replaces or clears an integration-scoped setting as one audited unit. The unit's first act after its own operation
    /// lock is the integration row lock, held to the commit, so the integration cannot be deleted between this change's
    /// decision reads and its commit (AUD-1.4).
    /// </summary>
    /// <param name="reauthorize">
    /// The caller's own authorization check, run again once the integration lock is held, so a decision taken before an
    /// ownership move committed is not acted on afterwards. <b>Required</b> from any caller whose authorization depends on the
    /// integration (its identity or owning resource context): it must repeat that decision against the integration as locked, and
    /// a caller must not leave it out to skip the revalidation. Null is legitimate only for a caller that made no
    /// integration-dependent decision. The settings endpoints pass it.
    /// </param>
    /// <exception cref="KeyNotFoundException">The integration does not exist (any more). Nothing was changed or recorded.</exception>
    Task<PlatformSettingResolvedValue> SetAsync(
        string integrationId,
        string key,
        PlatformSettingUpdatePayload payload,
        string? rowVersionBase64,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, ValueTask>? reauthorize = null);

    /// <summary>
    /// Removes an integration-scoped override as one audited unit, under the same integration row lock as
    /// <see cref="SetAsync"/>.
    /// </summary>
    /// <param name="reauthorize">As for <see cref="SetAsync"/>.</param>
    /// <exception cref="KeyNotFoundException">The integration does not exist (any more). Nothing was changed or recorded.</exception>
    Task<PlatformSettingResolvedValue?> ResetAsync(
        string integrationId,
        string key,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, ValueTask>? reauthorize = null);
}
