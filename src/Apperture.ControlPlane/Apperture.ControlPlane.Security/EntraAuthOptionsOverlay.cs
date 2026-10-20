using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>What <see cref="EntraAuthOptionsOverlay.ApplyAsync"/> did to the options.</summary>
public enum EntraOverlayOutcome
{
    /// <summary>There is no active, enabled registration: the options are untouched.</summary>
    NoActiveRegistration,

    /// <summary>The registration's values were applied but no client secret exists, so Entra stays disabled.</summary>
    AppliedWithoutSecret,

    /// <summary>The registration's values were applied and Entra was enabled.</summary>
    Applied,
}

/// <summary>
/// The rule that overlays the active Entra registration on the deployment options. It is the one place the rule lives: the host
/// applies it when it builds <see cref="EntraAuthOptions"/>, and the administrator-viability evaluation applies it to the
/// persisted registration to learn what the next start will run.
/// </summary>
public static class EntraAuthOptionsOverlay
{
    /// <summary>
    /// Applies <paramref name="active"/> to <paramref name="options"/>. The client secret is the registration's own stored secret
    /// first, then the deployment secret already in <paramref name="options"/>, as <see cref="EntraClientSecretAccess"/> resolves it.
    /// </summary>
    public static async ValueTask<EntraOverlayOutcome> ApplyAsync(
        EntraInstanceRegistration? active,
        EntraAuthOptions options,
        ISecretResolver? secretResolver,
        CancellationToken cancellationToken = default)
    {
        if (active is null || !active.Enabled)
            return EntraOverlayOutcome.NoActiveRegistration;

        options.Authority = active.Authority;
        options.ClientId = active.ClientId;
        if (!string.IsNullOrWhiteSpace(active.AzureTenantId))
            options.AzureTenantId = active.AzureTenantId;
        options.AuthenticationContextId = string.IsNullOrWhiteSpace(active.AuthenticationContextId)
            ? null
            : active.AuthenticationContextId.Trim();
        if (!string.IsNullOrWhiteSpace(active.CallbackPath))
            options.CallbackPath = active.CallbackPath!;

        var resolved = await EntraClientSecretAccess
            .TryResolveAsync(active, secretResolver, options.ClientSecret, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(resolved))
            options.ClientSecret = resolved;

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            return EntraOverlayOutcome.AppliedWithoutSecret;

        options.Enabled = true;
        return EntraOverlayOutcome.Applied;
    }
}
