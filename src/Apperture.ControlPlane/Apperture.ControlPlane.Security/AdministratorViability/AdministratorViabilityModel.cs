namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>
/// Which authentication the evaluation describes (IAM-10.2b, ruling D-4). A configuration change does not reach the running
/// authentication middleware until the host restarts, so a change can be safe in one view and not in the other. Every
/// invariant is applied to each view independently.
/// </summary>
public enum ViabilityView
{
    /// <summary>What the running host actually accepts: its own options, and the live tenant check at sign-in.</summary>
    Now,

    /// <summary>What the persisted configuration will accept once the host has restarted.</summary>
    Restart,
}

/// <summary>How far a reading of the deployment configuration can be relied on.</summary>
public enum ConfigurationTrustLevel
{
    /// <summary>The reading is authoritative for this installation.</summary>
    Trusted,

    /// <summary>The reading is missing or incomplete, so it cannot be established.</summary>
    Unknown,

    /// <summary>The reading is present and cannot be believed (unparseable, or failed validation).</summary>
    Untrusted,
}

/// <summary>
/// Whether the configuration inputs of an evaluation can be relied on, per input. Anything that is not trusted fails closed for a
/// viability-reducing operation (ruling D-6).
/// </summary>
/// <param name="Local">Whether Local sign-in is enabled for the installation.</param>
/// <param name="Entra">Whether the Entra options, including the client secret the active registration needs, are known.</param>
/// <param name="Reasons">Plain-language reasons for anything not trusted, for the operator.</param>
public sealed record ConfigurationTrust(
    ConfigurationTrustLevel Local,
    ConfigurationTrustLevel Entra,
    IReadOnlyList<string> Reasons)
{
    /// <summary>Everything is established.</summary>
    public static ConfigurationTrust Full { get; } = new(ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Trusted, []);

    /// <summary>True when Local sign-in availability is established.</summary>
    public bool LocalKnown => Local == ConfigurationTrustLevel.Trusted;

    /// <summary>True when Entra availability is established.</summary>
    public bool EntraKnown => Entra == ConfigurationTrustLevel.Trusted;
}

/// <summary>What one view of the authentication accepts, as far as administrator sign-in is concerned.</summary>
/// <param name="View">Which view.</param>
/// <param name="LocalSignInEnabled">Local sign-in is enabled. The direct Local login route depends on nothing else: the chooser flags never matter.</param>
/// <param name="EntraSignInUsable">Entra sign-in is enabled and its options are valid.</param>
/// <param name="AcceptedEntraTenantId">
/// The directory id a token must carry to be accepted, or null when no Entra token is accepted at all (no active registration,
/// an unresolvable tenant, or a running handler bound to a different tenant than the persisted registration).
/// </param>
/// <param name="IsEstimate">The view is inferred (the emergency command line cannot observe a running host's options).</param>
/// <param name="Notes">Anything the evaluation found worth reporting, such as two readers of the running options that disagree.</param>
public sealed record SignInAvailability(
    ViabilityView View,
    bool LocalSignInEnabled,
    bool EntraSignInUsable,
    string? AcceptedEntraTenantId,
    bool IsEstimate,
    IReadOnlyList<string> Notes);

/// <summary>The two views and the trust in the inputs that produced them.</summary>
public sealed record EffectiveSignInConfiguration(
    SignInAvailability Now,
    SignInAvailability Restart,
    ConfigurationTrust Trust);

/// <summary>The administrators who can sign in under one view.</summary>
/// <param name="View">Which view.</param>
/// <param name="ViableOrdinaryAdministrators">
/// VOA: holders of the Platform Administrator role who are not Recovery Access and have at least one usable sign-in method.
/// </param>
/// <param name="EntraIndependentAdministrators">
/// EI: the VOA who have an enabled Local credential while Local sign-in is enabled, so they do not depend on Entra.
/// </param>
public sealed record AdministratorViabilitySnapshot(
    ViabilityView View,
    IReadOnlySet<string> ViableOrdinaryAdministrators,
    IReadOnlySet<string> EntraIndependentAdministrators);

/// <summary>Everything the invariants are stated over, read in one state.</summary>
/// <param name="Now">Viability under the running host.</param>
/// <param name="Restart">Viability under the persisted configuration after a restart.</param>
/// <param name="RecoveryHealthy">Recovery Access is demonstrably healthy. It does not depend on the view.</param>
/// <param name="Trust">The trust in the configuration inputs.</param>
/// <param name="Configuration">The two availabilities the snapshots were evaluated under.</param>
public sealed record AdministratorViabilityState(
    AdministratorViabilitySnapshot Now,
    AdministratorViabilitySnapshot Restart,
    bool RecoveryHealthy,
    ConfigurationTrust Trust,
    EffectiveSignInConfiguration Configuration)
{
    /// <summary>The snapshot of one view.</summary>
    public AdministratorViabilitySnapshot For(ViabilityView view) => view == ViabilityView.Now ? Now : Restart;
}
