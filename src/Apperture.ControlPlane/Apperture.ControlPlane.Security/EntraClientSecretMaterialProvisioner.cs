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
    /// secret a registration already names: the store runs its own transaction, so the credential a registration resolves
    /// changes only when the registration row naming the new ref commits (AUD-1.2). The caller retires whichever secret
    /// ends up unused, and only once it knows whether that row committed.
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

    public async ValueTask TryDisableOwnedSecretAsync(
        string registrationId,
        string? secretRef,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretRef) || !Guid.TryParse(secretRef.Trim(), out var guid))
            return;

        var secretId = new SecretId(guid);
        var descriptor = await _secretStore.GetAsync(secretId, cancellationToken).ConfigureAwait(false);
        if (descriptor is null)
            return;

        var expectedName = BuildSecretName(registrationId);
        if (!string.Equals(descriptor.Name, expectedName, StringComparison.Ordinal)
            || !string.Equals(descriptor.Provider, SecretProvider, StringComparison.Ordinal))
            return;

        if (descriptor.State is SecretState.Disabled or SecretState.Revoked)
            return;

        await _secretStore.DisableAsync(secretId, cancellationToken).ConfigureAwait(false);
    }
}
