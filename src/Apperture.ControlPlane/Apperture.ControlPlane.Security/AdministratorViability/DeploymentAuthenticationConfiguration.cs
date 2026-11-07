using Apperture.Core.Application.AdministratorViability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>The options a running host is actually using, as its sign-in endpoints and handler read them.</summary>
/// <param name="Local">The Local options the login routes read.</param>
/// <param name="EntraEndpoints">The Entra options the sign-in, link and step-up endpoints gate on (fixed when the host built them).</param>
/// <param name="Handler">The configuration the OpenID Connect handler actually holds (read from the framework's own cache).</param>
/// <param name="EndpointsQuality">
/// How completely <paramref name="EntraEndpoints"/> was built from the persisted registration and its secret (R-1), or null when
/// nothing was recorded.
/// </param>
public sealed record RunningAuthenticationReading(
    LocalAuthOptions Local,
    EntraAuthOptions EntraEndpoints,
    RunningHandlerBinding Handler,
    EntraOptionsBuildQuality? EndpointsQuality = null);

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
/// <param name="Evidence">
/// Where the reading came from when it is not the host's own: <see cref="ConfigurationEvidence.Attested"/> for a fresh, consistent
/// attestation, <see cref="ConfigurationEvidence.Unavailable"/> when none could be trusted. Null for a reader that is the host.
/// </param>
/// <param name="EvidenceRemaining">
/// For an attested reading, how much of the freshness window was left when the database's clock judged it (zero when its age equalled
/// the window). It is what stops one attempt from relying on the reading for longer than the attestation was fresh
/// (<see cref="AttemptConfigurationReading"/>). Null for every other reading, which has no window.
/// </param>
public sealed record DeploymentAuthenticationReading(
    ConfigurationTrustLevel LocalTrust,
    ConfigurationTrustLevel EntraTrust,
    IReadOnlyList<string> Reasons,
    bool LocalSignInEnabled,
    EntraAuthOptions EntraBase,
    bool AuthoritativeAboutSecrets,
    RunningAuthenticationReading? Running,
    ConfigurationEvidence? Evidence = null,
    TimeSpan? EvidenceRemaining = null);

/// <summary>
/// Where the administrator-viability evaluation reads the deployment's authentication configuration (ruling D-6). The Management
/// Host reads its own running options. The emergency command line is another process and cannot observe them, so it reads the
/// configuration it was given and says how far that can be relied on.
/// </summary>
public interface IDeploymentAuthenticationConfiguration
{
    /// <summary>Reads the configuration now.</summary>
    DeploymentAuthenticationReading Read();

    /// <summary>
    /// Reads the configuration now, asynchronously. A reader whose evidence lives in the database (the emergency command line reads
    /// the Management Host's attestation) can only be read this way, and the evaluation asks this way, after the administrator-set
    /// gate is held and inside the caller's unit. A reader with nothing to wait for answers with <see cref="Read"/>.
    /// </summary>
    ValueTask<DeploymentAuthenticationReading> ReadAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read());

    /// <summary>
    /// Builds, if they are not built yet, whatever a reading is made from. A reading made for the first time builds the running
    /// Entra options, which reads the persisted registration and its secret through a connection of its own; made inside a
    /// transaction that has written a registration row, that read waits for the row or reads it as it was before. A writer of
    /// registrations calls this before it opens its unit (R-1). A reader with nothing to build does nothing.
    /// </summary>
    void EnsureInitialized()
    {
    }
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
    private readonly IRunningEntraHandlerBindingSource? _handler;
    private readonly IEntraOptionsBuildHealth? _health;

    /// <param name="handler">
    /// Where the running handler's configuration is read. A host that registers none cannot say what its handler does, which is
    /// reported as unavailable and never as usable.
    /// </param>
    /// <param name="health">Where the way the running Entra options were built is recorded (R-1). A host that registers none records nothing.</param>
    public HostDeploymentAuthenticationConfiguration(
        IConfiguration configuration,
        IOptions<LocalAuthOptions> local,
        IOptions<EntraAuthOptions> entra,
        IRunningEntraHandlerBindingSource? handler = null,
        IEntraOptionsBuildHealth? health = null)
    {
        _configuration = configuration;
        _local = local;
        _entra = entra;
        _handler = handler;
        _health = health;
    }

    /// <inheritdoc />
    public void EnsureInitialized() => _ = Read();

    /// <inheritdoc />
    public DeploymentAuthenticationReading Read()
    {
        var localBase = new LocalAuthOptions();
        _configuration.GetSection(LocalAuthOptions.SectionName).Bind(localBase);
        var entraBase = new EntraAuthOptions();
        _configuration.GetSection(EntraAuthOptions.SectionName).Bind(entraBase);

        try
        {
            var local = _local.Value;
            var entraEndpoints = _entra.Value;
            return new DeploymentAuthenticationReading(
                ConfigurationTrustLevel.Trusted,
                ConfigurationTrustLevel.Trusted,
                [],
                localBase.Enabled,
                entraBase,
                AuthoritativeAboutSecrets: true,
                new RunningAuthenticationReading(
                    local,
                    entraEndpoints,
                    _handler?.Read() ?? RunningHandlerBinding.Unavailable("This host registers no way to read its sign-in handler."),
                    _health?.QualityOf(entraEndpoints)));
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
