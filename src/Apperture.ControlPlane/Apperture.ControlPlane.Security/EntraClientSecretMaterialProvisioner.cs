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
    /// Creates a new secret or adds an active version to an existing GUID ref.
    /// </summary>
    public async ValueTask<(string SecretRef, bool CreatedNewSecret)> ReplaceAsync(
        string registrationId,
        string plaintext,
        string? existingSecretRef,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(registrationId))
            throw new ArgumentException("Registration id is required.", nameof(registrationId));
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
        return (descriptor.SecretId.Value.ToString("D"), CreatedNewSecret: true);
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
