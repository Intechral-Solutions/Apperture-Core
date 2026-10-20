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
/// host built them (<c>EntraRuntimeState.IsUsable</c> over <c>IOptions</c>), and the OpenID Connect handler is configured from
/// the options monitor's value. Entra counts as usable only when both are. The sign-in then compares the token's tenant with the
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
            .ApplyAsync(active, restartEntra, _secretResolver, cancellationToken)
            .ConfigureAwait(false);
        var restartNotes = new List<string>();
        if (overlay == EntraOverlayOutcome.AppliedWithoutSecret && !reading.AuthoritativeAboutSecrets
            && entraTrust == ConfigurationTrustLevel.Trusted)
        {
            // This reader cannot see the host's deployment secret or secret store, so "no secret" proves nothing.
            entraTrust = ConfigurationTrustLevel.Unknown;
            reasons.Add(
                "The active Entra registration's client secret cannot be established from here: it is held by the deployment configuration or the secret store of the Management Host.");
        }

        var restart = new SignInAvailability(
            ViabilityView.Restart,
            reading.LocalSignInEnabled,
            EntraUsable(restartEntra, reading.LocalSignInEnabled),
            EntraDirectoryTenantResolver.ResolveAcceptedTokenTenantId(active),
            IsEstimate: false,
            restartNotes);

        var now = reading.Running is null
            ? restart with { View = ViabilityView.Now, IsEstimate = true, Notes = ["The running host's options cannot be observed from this process; the persisted configuration stands in for them."] }
            : ReadNow(reading.Running, active);

        return new EffectiveSignInConfiguration(now, restart, new ConfigurationTrust(reading.LocalTrust, entraTrust, reasons));
    }

    private static SignInAvailability ReadNow(RunningAuthenticationReading running, EntraInstanceRegistration? active)
    {
        var notes = new List<string>();
        var local = running.Local.Enabled;

        var endpointUsable = EntraUsable(running.EntraEndpoints, local);
        var handlerUsable = EntraConfigurationState.IsUsable(running.EntraHandler);
        if (endpointUsable != handlerUsable)
        {
            notes.Add("The options the sign-in endpoints read and the options the OpenID Connect handler was built from disagree; Entra is counted as usable only when both are.");
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
            var handlerTenant = HandlerTenant(running.EntraHandler);
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

    private static string? HandlerTenant(EntraAuthOptions handler) =>
        EntraTokenClaims.Normalize(
            EntraAuthorityHelper.TryGetTenantSegmentFromAuthority(
                EntraAuthorityHelper.ToMicrosoftIdentityPlatformAuthority(handler.Authority, handler.AzureTenantId)));

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
