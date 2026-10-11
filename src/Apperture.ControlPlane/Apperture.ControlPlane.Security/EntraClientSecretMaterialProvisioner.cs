using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Stores the Entra confidential client secret in <see cref="ISecretStore"/>
/// and returns a GUID ref for <c>EntraInstanceRegistrations.ClientSecretRef</c>.
/// </summary>
public sealed class EntraClientSecretMaterialProvisioner
{
    public const string SecretProvider = "entra-registration";

    private readonly ISecretStore _secretStore;

    public EntraClientSecretMaterialProvisioner(ISecretStore secretStore) =>
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));

    public static string BuildSecretName(string registrationId) =>
        $"entra-registration:{registrationId}:client-secret";

    /// <summary>
    /// Stores <paramref name="plaintext"/> as a new secret of its own and returns its GUID ref. It never adds a version to a
    /// secret a registration already names, so the credential a registration resolves changes only with the registration row
    /// that names the new ref. Called inside the registration change's audited unit, the store joins the unit's transaction
    /// (AUD-1.4), so the secret, the row naming it and the retirement of the one it replaces commit or roll back together.
    /// </summary>
    public async ValueTask<string> StoreNewAsync(
        string registrationId,
        string plaintext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(registrationId))
            throw new ArgumentException("Registration id is required.", nameof(registrationId));
        if (string.IsNullOrWhiteSpace(plaintext))
            throw new ArgumentException("Secret value is required.", nameof(plaintext));

        var material = new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: plaintext.Trim());

        var descriptor = await _secretStore.CreateAsync(
                new SecretCreateRequest(
                    BuildSecretName(registrationId),
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
        return descriptor.SecretId.Value.ToString("D");
    }

    /// <summary>
    /// Disables the secret <paramref name="secretRef"/> names when this registration owns it and it is still usable. Returns
    /// true only when it disabled one; a missing, foreign or already retired secret is left alone.
    /// </summary>
    public async ValueTask<bool> TryDisableOwnedSecretAsync(
        string registrationId,
        string? secretRef,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretRef) || !Guid.TryParse(secretRef.Trim(), out var guid))
            return false;

        var secretId = new SecretId(guid);
        var descriptor = await _secretStore.GetAsync(secretId, cancellationToken).ConfigureAwait(false);
        if (descriptor is null)
            return false;

        var expectedName = BuildSecretName(registrationId);
        if (!string.Equals(descriptor.Name, expectedName, StringComparison.Ordinal)
            || !string.Equals(descriptor.Provider, SecretProvider, StringComparison.Ordinal))
            return false;

        if (descriptor.State is SecretState.Disabled or SecretState.Revoked)
            return false;

        await _secretStore.DisableAsync(secretId, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
