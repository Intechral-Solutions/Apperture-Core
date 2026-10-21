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

/// <summary>Where the client secret an overlaid set of options ended with came from.</summary>
public enum EntraSecretProvenance
{
    /// <summary>The options hold no client secret.</summary>
    None,

    /// <summary>The registration's own stored secret.</summary>
    StoredSecret,

    /// <summary>The deployment's secret, which the registration asks for (<c>UsesDeploymentSecret</c>).</summary>
    DeploymentSecretRequested,

    /// <summary>
    /// The deployment's secret, which the registration neither asks for nor replaces with one of its own. The host has always kept
    /// it in the options and enabled Entra with it; it is reported so the secret is not mistaken for the registration's.
    /// </summary>
    DeploymentSecretLegacyFallback,
}

/// <summary>What the overlay did, with the certainty and the origin of the client secret.</summary>
/// <param name="Outcome">What happened to the options.</param>
/// <param name="SecretStatus">How far the registration's own secret resolution can be relied on (<see cref="EntraClientSecretAccess.ResolveAsync"/>).</param>
/// <param name="Provenance">Where the secret the options ended with came from.</param>
public sealed record EntraOverlayResult(EntraOverlayOutcome Outcome, EntraClientSecretStatus SecretStatus, EntraSecretProvenance Provenance)
{
    /// <summary>The options were left alone.</summary>
    public static EntraOverlayResult NoActiveRegistration { get; } =
        new(EntraOverlayOutcome.NoActiveRegistration, EntraClientSecretStatus.Absent, EntraSecretProvenance.None);
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
        CancellationToken cancellationToken = default) =>
        (await ApplyDetailedAsync(active, options, secretResolver, cancellationToken).ConfigureAwait(false)).Outcome;

    /// <summary>
    /// <see cref="ApplyAsync"/>, reporting too whether the registration's secret was resolved with certainty and where the secret
    /// the options ended with came from. The options are changed exactly as <see cref="ApplyAsync"/> changes them.
    /// </summary>
    public static async ValueTask<EntraOverlayResult> ApplyDetailedAsync(
        EntraInstanceRegistration? active,
        EntraAuthOptions options,
        ISecretResolver? secretResolver,
        CancellationToken cancellationToken = default,
        EntraSecretFailureHandling failureHandling = EntraSecretFailureHandling.Contain)
    {
        if (active is null || !active.Enabled)
            return EntraOverlayResult.NoActiveRegistration;

        options.Authority = active.Authority;
        options.ClientId = active.ClientId;
        if (!string.IsNullOrWhiteSpace(active.AzureTenantId))
            options.AzureTenantId = active.AzureTenantId;
        options.AuthenticationContextId = string.IsNullOrWhiteSpace(active.AuthenticationContextId)
            ? null
            : active.AuthenticationContextId.Trim();
        if (!string.IsNullOrWhiteSpace(active.CallbackPath))
            options.CallbackPath = active.CallbackPath!;

        var resolution = await EntraClientSecretAccess
            .ResolveAsync(active, secretResolver, options.ClientSecret, cancellationToken, failureHandling)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(resolution.Secret))
            options.ClientSecret = resolution.Secret;

        var provenance = resolution.Source switch
        {
            EntraClientSecretSource.SecretStore => EntraSecretProvenance.StoredSecret,
            EntraClientSecretSource.DeploymentSecret => EntraSecretProvenance.DeploymentSecretRequested,
            _ => string.IsNullOrWhiteSpace(options.ClientSecret) ? EntraSecretProvenance.None : EntraSecretProvenance.DeploymentSecretLegacyFallback,
        };

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            return new EntraOverlayResult(EntraOverlayOutcome.AppliedWithoutSecret, resolution.Status, EntraSecretProvenance.None);

        options.Enabled = true;
        return new EntraOverlayResult(EntraOverlayOutcome.Applied, resolution.Status, provenance);
    }
}
