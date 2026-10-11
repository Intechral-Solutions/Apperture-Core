using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Stores operator-supplied sensitive platform setting values in <see cref="ISecretStore"/>
/// and returns GUID refs for <c>PlatformSettingValues</c>.
/// </summary>
public sealed class PlatformSettingSecretMaterialProvisioner
{
    public const string SecretProvider = "platform-setting";

    private readonly ISecretStore _secretStore;

    public PlatformSettingSecretMaterialProvisioner(ISecretStore secretStore) =>
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));

    public static string BuildSecretName(string settingKey) =>
        $"platform-setting:{settingKey}";

    /// <summary>
    /// Creates a new secret or adds an active version to an existing GUID ref. Does not mutate platform setting persistence.
    /// Called inside the setting change's audited unit, the store joins the unit's transaction (AUD-1.4), so the new secret or
    /// version, the setting row naming it and their rows commit or roll back together: a version added to the secret a setting
    /// already names is live only once the change commits, and a failed change leaves no version, no active version and no
    /// secret behind. No compensation is needed or attempted.
    /// </summary>
    /// <returns>Secret id GUID in "D" format, and whether a brand-new secret descriptor was created (the caller records <c>created</c> or <c>replaced</c>).</returns>
    public async ValueTask<(string SecretRef, bool CreatedNewSecret)> ReplaceAsync(
        string settingKey,
        string plaintext,
        string? existingSecretRef,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
            throw new ArgumentException("Secret value is required for replace.", nameof(plaintext));

        var material = new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: plaintext.Trim());

        if (!string.IsNullOrWhiteSpace(existingSecretRef)
            && Guid.TryParse(existingSecretRef.Trim(), out var existingGuid))
        {
            var secretId = new SecretId(existingGuid);
            var existing = await _secretStore.GetAsync(secretId, cancellationToken).ConfigureAwait(false);
            if (existing is not null
                && existing.State != SecretState.Disabled
                && existing.State != SecretState.Revoked)
            {
                var version = await _secretStore.AddVersionAsync(secretId, material, cancellationToken).ConfigureAwait(false);
                await _secretStore.ActivateVersionAsync(secretId, version, cancellationToken).ConfigureAwait(false);
                return (existingGuid.ToString("D"), CreatedNewSecret: false);
            }
        }

        var descriptor = await _secretStore.CreateAsync(
                new SecretCreateRequest(
                    BuildSecretName(settingKey),
                    SecretProvider,
                    SecretMaterialType.OpaqueString),
                cancellationToken)
            .ConfigureAwait(false);

        var newVersion = await _secretStore.AddVersionAsync(
                descriptor.SecretId,
                material,
                cancellationToken)
            .ConfigureAwait(false);

        await _secretStore.ActivateVersionAsync(descriptor.SecretId, newVersion, cancellationToken).ConfigureAwait(false);
        return (descriptor.SecretId.Value.ToString("D"), CreatedNewSecret: true);
    }

    /// <summary>
    /// Disables the secret <paramref name="secretRef"/> names when this setting owns it (its name matches
    /// <see cref="BuildSecretName"/>) and it is still usable; it never disables a config-supplied GUID that may be shared.
    /// Returns true only when it disabled one, so the caller records a retirement only when one happened. Called inside the
    /// change's audited unit, it commits or rolls back with the change.
    /// </summary>
    public async ValueTask<bool> TryDisableOwnedSecretAsync(
        string settingKey,
        string? secretRef,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretRef) || !Guid.TryParse(secretRef.Trim(), out var guid))
            return false;

        var secretId = new SecretId(guid);
        var descriptor = await _secretStore.GetAsync(secretId, cancellationToken).ConfigureAwait(false);
        if (descriptor is null)
            return false;

        var expectedName = BuildSecretName(settingKey);
        if (!string.Equals(descriptor.Name, expectedName, StringComparison.Ordinal)
            || !string.Equals(descriptor.Provider, SecretProvider, StringComparison.Ordinal))
            return false;

        if (descriptor.State is SecretState.Disabled or SecretState.Revoked)
            return false;

        await _secretStore.DisableAsync(secretId, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
