using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap;

/// <summary>
/// Persists bootstrap credential references (non-secret) via <see cref="IBootstrapConfigurationStore"/>.
/// Password/plaintext binding is deferred to Epic 108b.
/// </summary>
public sealed class FileStoreBootstrapCredentialManager : IBootstrapCredentialManager
{
    private const string PasswordBindingDeferredMessage =
        "SQL password credential binding is deferred (Epic 108b). Use bind-integrated-security, bind-keyvault-secret, or bind-managed-identity.";

    private readonly IBootstrapConfigurationStore _store;

    public FileStoreBootstrapCredentialManager(IBootstrapConfigurationStore store)
    {
        _store = store;
    }

    public async Task<BootstrapValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var doc = await _store.GetAsync(cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return new BootstrapValidationResult
            {
                Success = false,
                Errors = ["No bootstrap configuration document found."]
            };
        }

        if (doc.CredentialReference is not null)
        {
            return new BootstrapValidationResult { Success = true };
        }

        if (doc.Profile.AuthMode == BootstrapAuthMode.WindowsIntegrated)
        {
            return new BootstrapValidationResult { Success = true };
        }

        return new BootstrapValidationResult
        {
            Success = false,
            Errors =
            [
                "CredentialReference is required for this authentication mode, or configure Windows Integrated Security."
            ]
        };
    }

    public async Task UpdateAsync(BootstrapUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SensitivePayload is not null)
        {
            throw new BootstrapConfigurationPersistenceException(PasswordBindingDeferredMessage);
        }

        if (request.CredentialReference is null)
        {
            throw new BootstrapConfigurationPersistenceException(
                "CredentialReference is required for bootstrap credential update.");
        }

        var doc = await _store.GetAsync(cancellationToken).ConfigureAwait(false);
        var profile = doc?.Profile ?? request.Profile ?? new SqlConnectionBootstrapProfile
        {
            Server = "localhost",
            Database = "Apperture",
            AuthMode = MapAuthMode(request.CredentialReference.SourceType)
        };

        if (request.Profile is not null)
        {
            profile = request.Profile;
        }

        var updated = new BootstrapConfigurationDocument
        {
            Profile = profile,
            CredentialReference = request.CredentialReference,
            Version = doc?.Version
        };

        await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    public Task RotateAsync(BootstrapCredentialReference? reference, CancellationToken cancellationToken = default) =>
        throw new BootstrapConfigurationPersistenceException(
            "Bootstrap credential rotation is not implemented. Deferred to Epic 108b.");

    private static BootstrapAuthMode MapAuthMode(BootstrapCredentialSourceType sourceType) =>
        sourceType switch
        {
            BootstrapCredentialSourceType.WindowsIntegrated => BootstrapAuthMode.WindowsIntegrated,
            BootstrapCredentialSourceType.ManagedIdentity => BootstrapAuthMode.ManagedIdentity,
            BootstrapCredentialSourceType.KeyVaultReference => BootstrapAuthMode.SqlPassword,
            _ => BootstrapAuthMode.SqlPassword
        };
}
