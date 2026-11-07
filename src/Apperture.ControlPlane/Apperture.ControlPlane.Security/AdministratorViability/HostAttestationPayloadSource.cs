using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>Who a Management Host is: the host (stable across restarts) and this process (new at every start).</summary>
/// <param name="HostId">A stable id of the host: its machine, its content root and its application. Not a secret and not configurable.</param>
/// <param name="ProcessInstanceId">Names this process. A newer process of the same host replaces the row of an older one.</param>
/// <param name="ProcessStartedUtc">When this process started, by the host's clock. Informational only.</param>
public sealed record HostAttestationIdentity(string HostId, string ProcessInstanceId, DateTimeOffset ProcessStartedUtc)
{
    /// <summary>Derives the identity of the process that runs <paramref name="environment"/>.</summary>
    public static HostAttestationIdentity Create(IHostEnvironment environment, TimeProvider time)
    {
        var root = Path.GetFullPath(environment.ContentRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var material = $"{Environment.MachineName.ToUpperInvariant()}|{root.ToUpperInvariant()}|{environment.ApplicationName}";
        var hostId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..32];
        return new HostAttestationIdentity(hostId, Guid.NewGuid().ToString("N"), time.GetUtcNow());
    }
}

/// <summary>
/// Makes the reading a Management Host attests about itself, without building its Entra or OpenID Connect options before real
/// traffic has built them (afterwards the host's own reading may build the endpoints' options; see the second remark).
/// </summary>
/// <remarks>
/// <para>Local sign-in is a deployment fact: it is read from the host's configuration and its Local options, which build nothing
/// else. The Entra readings of the running host exist only once the host has built its options on real traffic, which the build
/// record shows (<see cref="IEntraOptionsBuildHealth.OpenIdConnectOptionsBuilt"/>). Until then the payload says
/// <i>not established</i>, which the evaluation reads as unknown. A flag from configuration is recorded as a configured value
/// (the deployment's own Entra options) and never as the reading of the handler.</para>
/// <para>Once the build record shows real traffic, the reading is the host's own, made by the host's own
/// <see cref="IDeploymentAuthenticationConfiguration"/>, so the attested Now view is the one the host evaluates. That reading asks
/// the framework for the running Entra options and the handler's; the handler's exist by then, and the endpoints' options are
/// built from the same sources by the same code on first use if real traffic has not asked yet, after the migrations
/// (see the residual risks of the attestation record).</para>
/// </remarks>
public sealed class HostAttestationPayloadSource
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopes;
    private readonly IEntraOptionsBuildHealth? _health;

    public HostAttestationPayloadSource(IConfiguration configuration, IServiceScopeFactory scopes, IEntraOptionsBuildHealth? health = null)
    {
        _configuration = configuration;
        _scopes = scopes;
        _health = health;
    }

    /// <summary>Reads what the host can truthfully say about itself right now.</summary>
    public AttestedAuthenticationPayload Build()
    {
        var localBase = new LocalAuthOptions();
        _configuration.GetSection(LocalAuthOptions.SectionName).Bind(localBase);
        var entraBase = new EntraAuthOptions();
        _configuration.GetSection(EntraAuthOptions.SectionName).Bind(entraBase);

        using var scope = _scopes.CreateScope();

        if (_health is not { OpenIdConnectOptionsBuilt: true })
        {
            // Real traffic has not reached the host. Nothing is built: not the Entra options, not the handler's.
            return AttestedAuthenticationPayload.NotEstablished(LocalNow(scope.ServiceProvider, localBase), localBase.Enabled, entraBase);
        }

        var deployment = scope.ServiceProvider.GetService<IDeploymentAuthenticationConfiguration>();
        var reading = deployment?.Read();
        return reading?.Running is null
            ? AttestedAuthenticationPayload.NotEstablished(LocalNow(scope.ServiceProvider, localBase), localBase.Enabled, entraBase)
            : AttestedAuthenticationPayload.Established(reading);
    }

    /// <summary>
    /// Local sign-in as the running host's options say. Options that fail their own validation cannot be what the host runs, so they
    /// are reported as disabled: a smaller set of viable administrators can only make a decision refuse more.
    /// </summary>
    private static bool LocalNow(IServiceProvider services, LocalAuthOptions configured)
    {
        var options = services.GetService<IOptions<LocalAuthOptions>>();
        if (options is null)
            return configured.Enabled;

        try
        {
            return options.Value.Enabled;
        }
        catch (OptionsValidationException)
        {
            return false;
        }
    }
}
