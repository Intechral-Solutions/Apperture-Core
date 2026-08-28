using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Resolves the Entra confidential client secret from <c>ISecretStore</c>, then deployment config.
/// Never logs secret material.
/// </summary>
public static class EntraClientSecretAccess
{
    public static bool IsConfigured(string? clientSecretRef, bool usesDeploymentSecret, bool deploymentSecretPresent) =>
        !string.IsNullOrWhiteSpace(clientSecretRef)
        || (usesDeploymentSecret && deploymentSecretPresent);

    public static async Task<string?> TryResolveAsync(
        EntraInstanceRegistration registration,
        ISecretResolver? secretResolver,
        string? deploymentSecret,
        CancellationToken cancellationToken = default)
    {
        if (registration is null)
            throw new ArgumentNullException(nameof(registration));

        if (!string.IsNullOrWhiteSpace(registration.ClientSecretRef) && secretResolver is not null)
        {
            try
            {
                var resolved = await secretResolver
                    .ResolveAsync(new SecretRef(registration.ClientSecretRef.Trim()), cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(resolved.Material.OpaqueString))
                    return resolved.Material.OpaqueString;
            }
            catch (Exception)
            {
                // Fall through to deployment secret. Callers log at their layer without the value.
            }
        }

        if (registration.UsesDeploymentSecret && !string.IsNullOrWhiteSpace(deploymentSecret))
            return deploymentSecret.Trim();

        return null;
    }
}
