using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>
/// Reads the authentication an administrator can sign in with, in the two views of ruling D-4, from the same inputs production
/// sign-in uses and none that it does not.
/// </summary>
public interface IEffectiveSignInConfigurationProvider
{
    /// <summary>
    /// Reads the configuration now. The persisted registration is read at this moment, so a caller that has applied a mutation in
    /// its transaction sees the <i>proposed</i> persisted state in the Restart view, and the tenant the sign-in will check after
    /// the commit in the Now view. A caller that must decide takes the administrator-set gate first.
    /// </summary>
    ValueTask<EffectiveSignInConfiguration> ReadAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IEffectiveSignInConfigurationProvider" />
/// <remarks>
/// <para><b>Now.</b> The running host's own options, read as its code reads them: the endpoints gate on the options fixed when the
/// host built them (<c>EntraRuntimeState.IsUsable</c> over <c>IOptions</c>), and the OpenID Connect handler holds the options the
/// framework built for it once and caches (<see cref="IRunningEntraHandlerBindingSource"/>), which a later reload of the Entra
/// options does not change. Entra counts as usable only when both are, and a handler whose configuration cannot be established is
/// never read as usable and makes the Entra reading unknown. The sign-in then compares the token's tenant with the
/// <i>live</i> active registration (<see cref="EntraDirectoryTenantResolver.ResolveAcceptedTokenTenantId"/>), so that tenant is
/// the accepted one, unless the running handler is bound to a different directory, in which case no token is accepted.</para>
/// <para><b>Restart.</b> The deployment's Entra options with the persisted active registration overlaid by
/// <see cref="EntraAuthOptionsOverlay"/>, the rule the host itself applies when it builds its options, and the persisted tenant.</para>
/// <para>The Local direct login route is available whenever <c>LocalAuth:Enabled</c> is, whatever the sign-in chooser shows.</para>
/// </remarks>
public sealed class EffectiveSignInConfigurationProvider : IEffectiveSignInConfigurationProvider
{
    private readonly IDeploymentAuthenticationConfiguration _deployment;
    private readonly IEntraInstanceRegistrationRepository _registrations;
    private readonly ISecretResolver? _secretResolver;

    public EffectiveSignInConfigurationProvider(
        IDeploymentAuthenticationConfiguration deployment,
        IEntraInstanceRegistrationRepository registrations,
        ISecretResolver? secretResolver = null)
    {
        _deployment = deployment;
        _registrations = registrations;
        _secretResolver = secretResolver;
    }

    /// <inheritdoc />
    public async ValueTask<EffectiveSignInConfiguration> ReadAsync(CancellationToken cancellationToken = default)
    {
        var reading = _deployment.Read();
        var active = await _registrations.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        var reasons = new List<string>(reading.Reasons);
        var entraTrust = reading.EntraTrust;

        // Restart: what the next start builds from the persisted registration.
        var restartEntra = Clone(reading.EntraBase);
        var overlay = await EntraAuthOptionsOverlay
            .ApplyDetailedAsync(active, restartEntra, _secretResolver, cancellationToken, EntraSecretFailureHandling.PropagateDatabaseFailures)
            .ConfigureAwait(false);
        var restartNotes = new List<string>();
        if (overlay.Outcome == EntraOverlayOutcome.AppliedWithoutSecret && !reading.AuthoritativeAboutSecrets
            && entraTrust == ConfigurationTrustLevel.Trusted)
        {
            // This reader cannot see the host's deployment secret or secret store, so "no secret" proves nothing.
            entraTrust = ConfigurationTrustLevel.Unknown;
            reasons.Add(
                "The active Entra registration's client secret cannot be established from here: it is held by the deployment configuration or the secret store of the Management Host.");
        }

        if (overlay.SecretStatus == EntraClientSecretStatus.Unavailable && entraTrust == ConfigurationTrustLevel.Trusted)
        {
            // The registration names a stored secret and the store could not be read: whether it exists, and which secret the
            // registration would use, is not established, so "no secret" and "a secret" are both guesses. The sign-in's own
            // fall-through to the deployment secret is unchanged and is not read as the answer.
            entraTrust = ConfigurationTrustLevel.Unknown;
            reasons.Add(
                "The active Entra registration's client secret could not be read from the secret store, so whether Microsoft sign-in works cannot be established. Try again when the secret store is available.");
        }

        if (overlay.Provenance == EntraSecretProvenance.DeploymentSecretLegacyFallback)
        {
            restartNotes.Add(
                "The active Entra registration does not ask for the deployment's client secret and holds none of its own; Microsoft sign-in is usable only through the deployment secret, as the host has always applied it.");
        }

        var restart = new SignInAvailability(
            ViabilityView.Restart,
            reading.LocalSignInEnabled,
            EntraUsable(restartEntra, reading.LocalSignInEnabled),
            EntraDirectoryTenantResolver.ResolveAcceptedTokenTenantId(active),
            IsEstimate: false,
            restartNotes);

        SignInAvailability now;
        if (reading.Running is null)
        {
            now = restart with { View = ViabilityView.Now, IsEstimate = true, Notes = ["The running host's options cannot be observed from this process; the persisted configuration stands in for them."] };
        }
        else
        {
            now = ReadNow(reading.Running, active);
            if (reading.Running.Handler.State == RunningHandlerState.Unavailable && entraTrust == ConfigurationTrustLevel.Trusted)
            {
                // Never read an unknown handler as usable, and never read it as unusable either: what Entra can do is unknown.
                entraTrust = ConfigurationTrustLevel.Unknown;
                reasons.Add(
                    "The configuration of the running sign-in handler cannot be established" +
                    (string.IsNullOrWhiteSpace(reading.Running.Handler.Detail) ? "." : $" ({reading.Running.Handler.Detail})."));
            }

            if ((reading.Running.EndpointsQuality == EntraOptionsBuildQuality.Degraded
                 || reading.Running.Handler.Quality == EntraOptionsBuildQuality.Degraded)
                && entraTrust == ConfigurationTrustLevel.Trusted)
            {
                // R-1: the framework built the running options while the registration or its secret could not be read, and keeps
                // them until it rebuilds them. They are what the host runs, but they were not built from the persisted
                // configuration, so what the host accepts now is not a settled reading.
                entraTrust = ConfigurationTrustLevel.Unknown;
                reasons.Add(
                    "The running sign-in options were built while the Microsoft registration or its client secret could not be read, so what the host accepts now is not established until the host is restarted once they can be read.");
            }
        }

        return new EffectiveSignInConfiguration(now, restart, new ConfigurationTrust(reading.LocalTrust, entraTrust, reasons));
    }

    private static SignInAvailability ReadNow(RunningAuthenticationReading running, EntraInstanceRegistration? active)
    {
        var notes = new List<string>();
        var local = running.Local.Enabled;

        var endpointUsable = EntraUsable(running.EntraEndpoints, local);
        var handlerUsable = running.Handler.IsUsable;
        if (running.Handler.State == RunningHandlerState.Unavailable)
        {
            notes.Add("The running sign-in handler's configuration is unavailable; Entra is not counted as usable.");
        }
        else if (endpointUsable != handlerUsable)
        {
            notes.Add("The options the sign-in endpoints read and the configuration the OpenID Connect handler holds disagree; Entra is counted as usable only when both are.");
        }

        if (endpointUsable != EntraConfigurationState.IsUsable(running.EntraEndpoints))
        {
            notes.Add("The merged sign-in snapshot and EntraConfigurationState.IsUsable disagree about the running options (the authentication context id); the snapshot, which is the stricter, is used.");
        }

        var usable = endpointUsable && handlerUsable;
        var liveTenant = EntraDirectoryTenantResolver.ResolveAcceptedTokenTenantId(active);
        string? accepted = null;
        if (usable && liveTenant is not null)
        {
            var handlerTenant = HandlerTenant(running.Handler);
            if (TenantsConflict(handlerTenant, liveTenant))
            {
                notes.Add(
                    "The running OpenID Connect handler is bound to a different directory than the persisted active registration: no Entra token is accepted until the host restarts.");
            }
            else
            {
                accepted = liveTenant;
            }
        }

        return new SignInAvailability(ViabilityView.Now, local, usable, accepted, IsEstimate: false, notes);
    }

    /// <summary>Entra sign-in is enabled and its options are valid: the merged snapshot's own answer (the single definition).</summary>
    private static bool EntraUsable(EntraAuthOptions entra, bool localEnabled) =>
        AuthSignInOptionsMerger.Merge(new LocalAuthOptions { Enabled = localEnabled }, entra, new InstanceAuthUiPolicy()).EntraSignInEnabled;

    /// <summary>
    /// The directory the handler signs users in through: the tenant segment of the authority it holds. That authority is already the
    /// tenant-specific form the host built for it, so nothing is derived again here.
    /// </summary>
    private static string? HandlerTenant(RunningHandlerBinding handler) =>
        EntraTokenClaims.Normalize(EntraAuthorityHelper.TryGetTenantSegmentFromAuthority(handler.Authority));

    /// <summary>Two concrete directory ids that differ. A domain name or a multi-tenant alias is not compared: the live tenant decides.</summary>
    private static bool TenantsConflict(string? handlerTenant, string liveTenant) =>
        EntraAuthorityHelper.IsWellFormedGuid(handlerTenant)
        && EntraAuthorityHelper.IsWellFormedGuid(liveTenant)
        && !string.Equals(handlerTenant, liveTenant, StringComparison.OrdinalIgnoreCase);

    private static EntraAuthOptions Clone(EntraAuthOptions source) => new()
    {
        Enabled = source.Enabled,
        Authority = source.Authority,
        ClientId = source.ClientId,
        ClientSecret = source.ClientSecret,
        AzureTenantId = source.AzureTenantId,
        CallbackPath = source.CallbackPath,
        Scopes = [.. source.Scopes],
        AuthenticationContextId = source.AuthenticationContextId,
        DevelopmentSimulateAuthenticationContext = source.DevelopmentSimulateAuthenticationContext,
    };
}
