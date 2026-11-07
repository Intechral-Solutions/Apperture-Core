using System.Text.Json;
using System.Text.Json.Serialization;
using Apperture.Core.Application.AdministratorViability;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>The timing of the attestation, fixed in code on purpose: a deployment knob could only lengthen the window and weaken it.</summary>
public static class HostConfigurationAttestationTiming
{
    /// <summary>How often a running host refreshes its attestation.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long after its last refresh, by the database's clock, a reading still counts as fresh: three refresh intervals. A reading
    /// whose age equals the window is fresh; one tick older is not. The window bounds how stale a reading can be; it does not make
    /// the reading true at the moment it is used (a host can stop or reload after its last refresh).
    /// </summary>
    public static readonly TimeSpan FreshnessWindow = HostConfigurationAttestationLiveness.FreshnessWindow;
}

/// <summary>
/// The Entra options a host reports, as an explicit allow-list of the non-secret fields the sign-in decision reads. The client
/// secret is reported only as present or absent: there is no field that could carry its value.
/// </summary>
public sealed record AttestedEntraOptions(
    bool Enabled,
    string Authority,
    string ClientId,
    bool HasClientSecret,
    string? AzureTenantId,
    string CallbackPath,
    string[] Scopes,
    string? AuthenticationContextId,
    bool DevelopmentSimulateAuthenticationContext)
{
    /// <summary>The marker a rebuilt options object carries where the host holds a secret. It is not a secret and is never sent anywhere.</summary>
    public const string SecretPresentMarker = "(held by the Management Host)";

    /// <summary>Reports <paramref name="options"/> without its secret.</summary>
    public static AttestedEntraOptions From(EntraAuthOptions options) => new(
        options.Enabled,
        options.Authority ?? "",
        options.ClientId ?? "",
        !string.IsNullOrWhiteSpace(options.ClientSecret),
        options.AzureTenantId,
        options.CallbackPath ?? "",
        [.. options.Scopes ?? []],
        options.AuthenticationContextId,
        options.DevelopmentSimulateAuthenticationContext);

    /// <summary>The options this report describes, with the marker where the host holds a secret.</summary>
    public EntraAuthOptions ToOptions() => new()
    {
        Enabled = Enabled,
        Authority = Authority,
        ClientId = ClientId,
        ClientSecret = HasClientSecret ? SecretPresentMarker : "",
        AzureTenantId = AzureTenantId,
        CallbackPath = CallbackPath,
        Scopes = [.. Scopes ?? []],
        AuthenticationContextId = AuthenticationContextId,
        DevelopmentSimulateAuthenticationContext = DevelopmentSimulateAuthenticationContext,
    };
}

/// <summary>The sign-in handler a host reports (the same facts <see cref="RunningHandlerBinding"/> carries: no secret value).</summary>
public sealed record AttestedHandler(
    RunningHandlerState State,
    string? Authority,
    string? ClientId,
    bool HasClientSecret,
    EntraOptionsBuildQuality? Quality)
{
    /// <summary>Reports <paramref name="binding"/>.</summary>
    public static AttestedHandler From(RunningHandlerBinding binding) =>
        new(binding.State, binding.Authority, binding.ClientId, binding.HasClientSecret, binding.Quality);

    /// <summary>The binding this report describes.</summary>
    public RunningHandlerBinding ToBinding() => new(State, Authority, ClientId, HasClientSecret, Detail: null, Quality);
}

/// <summary>
/// The Entra readings of the running host: only present when the host has built its Entra and OpenID Connect options on real
/// traffic. Their absence is the state "not established", which is unknown and never usable.
/// </summary>
public sealed record AttestedEntraNow(
    AttestedEntraOptions Endpoints,
    EntraOptionsBuildQuality? EndpointsQuality,
    AttestedHandler Handler);

/// <summary>
/// What a Management Host says about its own authentication configuration, schema version 1. Local sign-in is a deployment fact the
/// host can read without building anything; the Entra readings of the running host exist only once its options were built by real
/// traffic. The restart side is the deployment's own Entra options, from which the command line recomputes what a restart would
/// run, over the registration it reads from the database itself.
/// </summary>
/// <param name="LocalNow">Local sign-in as the running host's options say.</param>
/// <param name="LocalRestart">Local sign-in as the host's configuration says (what a restart starts from).</param>
/// <param name="EntraBase">The deployment's own Entra options, before any registration is overlaid on them (non-secret fields only).</param>
/// <param name="EntraNow">The running host's Entra readings, or null when they are not established.</param>
public sealed record AttestedAuthenticationPayload(
    bool LocalNow,
    bool LocalRestart,
    AttestedEntraOptions EntraBase,
    AttestedEntraNow? EntraNow)
{
    /// <summary>The version of this shape. A reader that does not know a row's version does not read it.</summary>
    public const int SchemaVersion = 1;

    /// <summary>The payload's largest accepted size (characters). A larger document is not read.</summary>
    public const int MaxLength = 64 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>The payload for a host whose running Entra options were not built (or cannot be read yet).</summary>
    public static AttestedAuthenticationPayload NotEstablished(bool localNow, bool localRestart, EntraAuthOptions entraBase) =>
        new(localNow, localRestart, AttestedEntraOptions.From(entraBase), EntraNow: null);

    /// <summary>The payload for a host that has built its running options, from the reading it just made.</summary>
    public static AttestedAuthenticationPayload Established(DeploymentAuthenticationReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        var running = reading.Running ?? throw new ArgumentException("An established payload needs the running host's reading.", nameof(reading));
        return new(
            running.Local.Enabled,
            reading.LocalSignInEnabled,
            AttestedEntraOptions.From(reading.EntraBase),
            new AttestedEntraNow(
                AttestedEntraOptions.From(running.EntraEndpoints),
                running.EndpointsQuality,
                AttestedHandler.From(running.Handler)));
    }

    /// <summary>The stored document.</summary>
    public string Serialize() => JsonSerializer.Serialize(this, Json);

    /// <summary>Reads a stored document. Anything but exactly this shape (an unknown member, an unknown name, a null part) is not read.</summary>
    public static bool TryParse(string? document, out AttestedAuthenticationPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(document) || document.Length > MaxLength)
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<AttestedAuthenticationPayload>(document, Json);
            if (parsed is null || parsed.EntraBase is null || parsed.EntraBase.Scopes is null)
                return false;
            if (parsed.EntraNow is { } now && (now.Endpoints is null || now.Endpoints.Scopes is null || now.Handler is null))
                return false;

            payload = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// The reading this payload describes, in the shape the viability evaluation already consumes, so that the evaluation is the same
    /// code whether the host or the command line asks. It says nothing the host did not say: where the host's running options were not
    /// established, the handler is unavailable, which the evaluation reads as unknown, never as usable.
    /// </summary>
    public DeploymentAuthenticationReading ToReading()
    {
        var running = EntraNow is { } now
            ? new RunningAuthenticationReading(
                new LocalAuthOptions { Enabled = LocalNow },
                now.Endpoints.ToOptions(),
                now.Handler.ToBinding(),
                now.EndpointsQuality)
            : new RunningAuthenticationReading(
                new LocalAuthOptions { Enabled = LocalNow },
                new EntraAuthOptions(),
                RunningHandlerBinding.Unavailable("the Management Host has not built its Microsoft sign-in options yet"),
                EndpointsQuality: null);

        return new DeploymentAuthenticationReading(
            ConfigurationTrustLevel.Trusted,
            ConfigurationTrustLevel.Trusted,
            [],
            LocalRestart,
            EntraBase.ToOptions(),
            // The command line cannot see the host's deployment secret or its secret store, so "no secret" proves nothing here.
            AuthoritativeAboutSecrets: false,
            running,
            ConfigurationEvidence.Attested);
    }
}
