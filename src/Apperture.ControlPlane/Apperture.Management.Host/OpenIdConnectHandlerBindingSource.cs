using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Apperture.Management.Host;

/// <summary>
/// Reads what the running OpenID Connect handler holds, from the named options the framework hands that handler.
/// </summary>
/// <remarks>
/// <para>The authentication handler asks <c>IOptionsMonitor&lt;OpenIdConnectOptions&gt;.Get(scheme)</c> for its options on every
/// request. The monitor builds them once (running <see cref="EntraOpenIdConnectOptionsConfigurer"/> with the Entra options of that
/// moment), keeps them in its cache, and builds them again only when the cache is invalidated by a change token source for
/// <see cref="OpenIdConnectOptions"/>. A reload of the Entra options refreshes <c>IOptionsMonitor&lt;EntraAuthOptions&gt;</c> and
/// nothing else, so the Entra monitor can describe a configuration the handler does not hold. The handler's own named options are
/// therefore the one authority, and this reads exactly the instance the handler is given.</para>
/// <para>Reading does not change what the handler will do. The authentication middleware asks for these options at the start of
/// every request, because the OpenID Connect handler handles requests (the callback path), so by the time any request reaches code
/// that reads this they exist and are the ones in use. Read outside a request, the monitor would build them as the first request
/// would, from the same Entra options.</para>
/// </remarks>
internal sealed class OpenIdConnectHandlerBindingSource : IRunningEntraHandlerBindingSource
{
    private readonly IOptionsMonitor<OpenIdConnectOptions> _options;
    private readonly IEntraOptionsBuildHealth? _health;

    public OpenIdConnectHandlerBindingSource(IOptionsMonitor<OpenIdConnectOptions> options, IEntraOptionsBuildHealth? health = null)
    {
        _options = options;
        _health = health;
    }

    /// <inheritdoc />
    public RunningHandlerBinding Read()
    {
        OpenIdConnectOptions options;
        try
        {
            options = _options.Get(OpenIdConnectDefaults.AuthenticationScheme);
        }
        catch (Exception ex)
        {
            // Never guessed at, and never carrying the message: a configuration failure could quote option values.
            return RunningHandlerBinding.Unavailable($"the handler's options could not be built ({ex.GetType().Name})");
        }

        // How the Entra options this handler was configured from were built (R-1), recorded when the configurer ran.
        var quality = _health?.QualityOf(options);

        if (string.Equals(options.ClientId, EntraOpenIdConnectOptionsConfigurer.DisabledPlaceholder, StringComparison.Ordinal)
            || string.Equals(options.ClientSecret, EntraOpenIdConnectOptionsConfigurer.DisabledPlaceholder, StringComparison.Ordinal))
        {
            return RunningHandlerBinding.Disabled() with { Quality = quality };
        }

        return RunningHandlerBinding.Bound(
            options.Authority,
            options.ClientId,
            hasClientSecret: !string.IsNullOrWhiteSpace(options.ClientSecret)) with { Quality = quality };
    }
}
