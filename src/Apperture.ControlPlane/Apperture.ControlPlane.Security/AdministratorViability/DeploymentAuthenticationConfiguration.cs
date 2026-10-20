using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>The options a running host is actually using, as its sign-in endpoints and handler read them.</summary>
/// <param name="Local">The Local options the login routes read.</param>
/// <param name="EntraEndpoints">The Entra options the sign-in, link and step-up endpoints gate on (fixed when the host built them).</param>
/// <param name="EntraHandler">The Entra options the OpenID Connect handler was configured from.</param>
public sealed record RunningAuthenticationReading(
    LocalAuthOptions Local,
    EntraAuthOptions EntraEndpoints,
    EntraAuthOptions EntraHandler);

/// <summary>One reading of the deployment's authentication configuration.</summary>
/// <param name="LocalTrust">How far <paramref name="LocalSignInEnabled"/> can be relied on.</param>
/// <param name="EntraTrust">How far the Entra readings can be relied on.</param>
/// <param name="Reasons">Why anything is not trusted, in words an operator can act on.</param>
/// <param name="LocalSignInEnabled">Local sign-in as the deployment configures it.</param>
/// <param name="EntraBase">
/// The deployment's own Entra options, before any registration is overlaid on them: what a restart starts from.
/// </param>
/// <param name="AuthoritativeAboutSecrets">
/// True when an absent client secret really is absent (the host holds the deployment secret and the secret store). False when
/// the reader cannot see them, so a missing secret proves nothing.
/// </param>
/// <param name="Running">The running host's options, or null when the reader is not a running host.</param>
public sealed record DeploymentAuthenticationReading(
    ConfigurationTrustLevel LocalTrust,
    ConfigurationTrustLevel EntraTrust,
    IReadOnlyList<string> Reasons,
    bool LocalSignInEnabled,
    EntraAuthOptions EntraBase,
    bool AuthoritativeAboutSecrets,
    RunningAuthenticationReading? Running);

/// <summary>
/// Where the administrator-viability evaluation reads the deployment's authentication configuration (ruling D-6). The Management
/// Host reads its own running options. The emergency command line is another process and cannot observe them, so it reads the
/// configuration it was given and says how far that can be relied on.
/// </summary>
public interface IDeploymentAuthenticationConfiguration
{
    /// <summary>Reads the configuration now.</summary>
    DeploymentAuthenticationReading Read();
}

/// <summary>
/// The Management Host's reading. The configuration is the host's own, so it is authoritative; the running options are the ones
/// its login routes, endpoints and OpenID Connect handler use.
/// </summary>
public sealed class HostDeploymentAuthenticationConfiguration : IDeploymentAuthenticationConfiguration
{
    private readonly IConfiguration _configuration;
    private readonly IOptions<LocalAuthOptions> _local;
    private readonly IOptions<EntraAuthOptions> _entra;
    private readonly IOptionsMonitor<EntraAuthOptions> _entraMonitor;

    public HostDeploymentAuthenticationConfiguration(
        IConfiguration configuration,
        IOptions<LocalAuthOptions> local,
        IOptions<EntraAuthOptions> entra,
        IOptionsMonitor<EntraAuthOptions> entraMonitor)
    {
        _configuration = configuration;
        _local = local;
        _entra = entra;
        _entraMonitor = entraMonitor;
    }

    /// <inheritdoc />
    public DeploymentAuthenticationReading Read()
    {
        var localBase = new LocalAuthOptions();
        _configuration.GetSection(LocalAuthOptions.SectionName).Bind(localBase);
        var entraBase = new EntraAuthOptions();
        _configuration.GetSection(EntraAuthOptions.SectionName).Bind(entraBase);

        try
        {
            return new DeploymentAuthenticationReading(
                ConfigurationTrustLevel.Trusted,
                ConfigurationTrustLevel.Trusted,
                [],
                localBase.Enabled,
                entraBase,
                AuthoritativeAboutSecrets: true,
                new RunningAuthenticationReading(_local.Value, _entra.Value, _entraMonitor.CurrentValue));
        }
        catch (OptionsValidationException ex)
        {
            // The host's own options failed validation, so what it is running cannot be read.
            return new DeploymentAuthenticationReading(
                ConfigurationTrustLevel.Trusted,
                ConfigurationTrustLevel.Untrusted,
                [$"The running authentication options failed validation: {ex.Message}"],
                localBase.Enabled,
                entraBase,
                AuthoritativeAboutSecrets: true,
                Running: null);
        }
    }
}

/// <summary>
/// The emergency command line's reading: the configuration the command line was started with (its <c>appsettings.json</c> and
/// environment), which names no installation. It is therefore only as reliable as the operator's having set the installation's
/// values, and it says so: a missing or unparseable <c>LocalAuth:Enabled</c> is not trusted, and it can never claim that a client
/// secret is absent, because it cannot see the host's deployment secret or secret store.
/// </summary>
public sealed class ConfiguredDeploymentAuthenticationConfiguration : IDeploymentAuthenticationConfiguration
{
    /// <summary>The configuration key the command line must carry to be trusted about Local sign-in.</summary>
    public const string LocalEnabledKey = LocalAuthOptions.SectionName + ":Enabled";

    private readonly IConfiguration _configuration;

    public ConfiguredDeploymentAuthenticationConfiguration(IConfiguration configuration) => _configuration = configuration;

    /// <inheritdoc />
    public DeploymentAuthenticationReading Read()
    {
        var reasons = new List<string>();
        var localTrust = ConfigurationTrustLevel.Trusted;
        var localEnabled = false;

        var raw = _configuration[LocalEnabledKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            localTrust = ConfigurationTrustLevel.Unknown;
            reasons.Add(
                $"The emergency command line does not know whether Local sign-in is enabled for this installation. Set {LocalEnabledKey} " +
                "(for example the environment variable LocalAuth__Enabled) to the installation's value and run the command again.");
        }
        else if (!bool.TryParse(raw.Trim(), out localEnabled))
        {
            localTrust = ConfigurationTrustLevel.Untrusted;
            reasons.Add($"{LocalEnabledKey} is set to '{raw}', which is not true or false.");
        }

        var entraBase = new EntraAuthOptions();
        _configuration.GetSection(EntraAuthOptions.SectionName).Bind(entraBase);

        return new DeploymentAuthenticationReading(
            localTrust,
            ConfigurationTrustLevel.Trusted,
            reasons,
            localEnabled,
            entraBase,
            AuthoritativeAboutSecrets: false,
            Running: null);
    }
}
