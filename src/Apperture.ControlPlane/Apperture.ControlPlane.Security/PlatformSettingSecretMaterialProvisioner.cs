using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Stores operator-supplied sensitive setting values in <see cref="ISecretStore"/> and returns GUID refs for
/// <c>PlatformSettingValues</c>. It acts on a secret only after the secret's own stored descriptor proves it belongs to the
/// setting (<see cref="Apperture.Core.Application.PlatformSettings.SettingSecretOwnership"/>): a reference in a setting row names
/// a secret and says nothing about who owns it.
/// </summary>
public sealed class PlatformSettingSecretMaterialProvisioner
{
    private readonly ISecretStore _secretStore;

    public PlatformSettingSecretMaterialProvisioner(ISecretStore secretStore) =>
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));

    /// <summary>
    /// Adds an active version to the secret <paramref name="existingSecretRef"/> names when that secret is <paramref name="owner"/>'s
    /// own and still usable. In every other case the setting names nothing it may version, and the value is stored as a new secret
    /// <paramref name="owner"/> owns: a missing, blank, unreadable or dangling reference, a disabled secret of its own, and any
    /// secret of another holder, provider or name (another integration's, an instance setting's, a connection's, an Entra
    /// registration's, the legacy shape). The secret the setting named before is never read, versioned, adopted, disabled or
    /// otherwise changed: only its descriptor is looked at, to learn that it is not the setting's own. The caller repoints the row.
    /// Called inside the setting change's audited unit, the store joins the unit's transaction (AUD-1.4), so the new secret or
    /// version, the setting row naming it and their rows commit or roll back together. No compensation is needed or attempted.
    /// </summary>
    /// <returns>Secret id GUID in "D" format, and whether a brand-new secret descriptor was created (the caller records <c>created</c> or <c>replaced</c>).</returns>
    public async ValueTask<(string SecretRef, bool CreatedNewSecret)> ReplaceAsync(
        SecretOwner owner,
        string plaintext,
        string? existingSecretRef,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (string.IsNullOrWhiteSpace(plaintext))
            throw new ArgumentException("Secret value is required for replace.", nameof(plaintext));

        var material = new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: plaintext.Trim());

        var existing = await FindOwnedAsync(owner, existingSecretRef, cancellationToken).ConfigureAwait(false);
        if (existing is { } owned && owned.State is not (SecretState.Disabled or SecretState.Revoked))
        {
            var version = await _secretStore.AddVersionAsync(owned.SecretId, material, cancellationToken).ConfigureAwait(false);
            await _secretStore.ActivateVersionAsync(owned.SecretId, version, cancellationToken).ConfigureAwait(false);
            return (owned.SecretId.Value.ToString("D"), CreatedNewSecret: false);
        }

        var descriptor = await _secretStore.CreateAsync(
                new SecretCreateRequest(owner.Name, owner.Provider, SecretMaterialType.OpaqueString),
                cancellationToken)
            .ConfigureAwait(false);

        var newVersion = await _secretStore.AddVersionAsync(descriptor.SecretId, material, cancellationToken).ConfigureAwait(false);
        await _secretStore.ActivateVersionAsync(descriptor.SecretId, newVersion, cancellationToken).ConfigureAwait(false);
        return (descriptor.SecretId.Value.ToString("D"), CreatedNewSecret: true);
    }

    /// <summary>
    /// Disables the secret <paramref name="secretRef"/> names when it is <paramref name="owner"/>'s own (its provider and name match
    /// exactly) and still usable; it never disables a secret of another holder, a config-supplied GUID that may be shared, or one
    /// whose ownership the descriptor cannot prove. Returns true only when it disabled one, so the caller records a retirement
    /// only when one happened. Called inside the change's audited unit, it commits or rolls back with the change.
    /// </summary>
    public async ValueTask<bool> TryDisableOwnedSecretAsync(
        SecretOwner owner,
        string? secretRef,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var descriptor = await FindOwnedAsync(owner, secretRef, cancellationToken).ConfigureAwait(false);
        if (descriptor is not { } owned || owned.State is SecretState.Disabled or SecretState.Revoked)
            return false;

        await _secretStore.DisableAsync(owned.SecretId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="reference"/> names a secret <paramref name="owner"/> owns. Only the descriptor is read, never a
    /// version or material, and the answer is the same for a missing secret, another provider and another owner: no.
    /// </summary>
    public async ValueTask<bool> IsReferenceOwnedAsync(SecretOwner owner, string? reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return await FindOwnedAsync(owner, reference, cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <summary>The descriptor of the secret <paramref name="reference"/> names, only if its provider and name are exactly <paramref name="owner"/>'s.</summary>
    private async ValueTask<SecretDescriptor?> FindOwnedAsync(SecretOwner owner, string? reference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reference) || !Guid.TryParse(reference.Trim(), out var guid))
            return null;

        var descriptor = await _secretStore.GetAsync(new SecretId(guid), cancellationToken).ConfigureAwait(false);
        return descriptor is not null && owner.Matches(descriptor.Provider, descriptor.Name) ? descriptor : null;
    }
}
