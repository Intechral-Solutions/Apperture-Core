using Apperture.Core.Application.AdministratorViability;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>
/// The emergency command line's reading of the deployment's authentication configuration (ruling D-6): the attestation the
/// Management Host wrote to the database, and nothing else. The command line's own <c>appsettings.json</c> and environment name no
/// installation and are not evidence about it.
/// </summary>
/// <remarks>
/// <para><b>One authoritative source.</b> There is no operator-supplied value, no override and no fallback to the command line's own
/// configuration. When the attestation is missing, stale, stopped, for another database, in a form this reader does not know,
/// unreadable or in conflict, the reading is unavailable: neither Local nor Entra is trusted, so a viability decision that depends on
/// either fails closed, and one that depends on neither is not affected.</para>
/// <para><b>Read where the decision is made.</b> The viability evaluation asks through <see cref="ReadAsync"/>, inside the audited
/// unit after the administrator-set gate is held, so the read joins the unit's transaction. The freshness is judged by the
/// database's clock, which the same statement returns.</para>
/// <para><b>What it does not do.</b> It validates nothing the host reported and builds nothing: the evaluation reads the reported
/// options exactly as it reads a running host's own. It is not proof that the host still runs as it reported (the window bounds that
/// risk, it does not remove it), and it is no defence against a writer of the table.</para>
/// </remarks>
public sealed class AttestedDeploymentAuthenticationConfiguration : IDeploymentAuthenticationConfiguration
{
    private readonly IHostConfigurationAttestationStore _store;
    private readonly TimeSpan _window;

    public AttestedDeploymentAuthenticationConfiguration(IHostConfigurationAttestationStore store, TimeSpan? freshnessWindow = null)
    {
        _store = store;
        _window = freshnessWindow ?? HostConfigurationAttestationTiming.FreshnessWindow;
    }

    /// <summary>
    /// A synchronous read would block on the database inside a unit. It is refused as an unavailable reading, not by throwing, so a
    /// caller that wrongly asks this way fails closed instead of crashing.
    /// </summary>
    public DeploymentAuthenticationReading Read() =>
        UnavailableReading("The Management Host's recorded sign-in configuration can only be read asynchronously.");

    /// <inheritdoc />
    public async ValueTask<DeploymentAuthenticationReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        var assessment = await AssessAsync(cancellationToken).ConfigureAwait(false);
        return assessment.Reading ?? UnavailableReading(assessment.Reason);
    }

    /// <summary>The verdict on the attestation, with the reason an operator can act on when it is not usable.</summary>
    public async ValueTask<AttestationAssessment> AssessAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        return HostConfigurationAttestationClassifier.Assess(snapshot, _window);
    }

    private static DeploymentAuthenticationReading UnavailableReading(string? reason) => new(
        ConfigurationTrustLevel.Unknown,
        ConfigurationTrustLevel.Unknown,
        [reason ?? "The Management Host's sign-in configuration is not available."],
        LocalSignInEnabled: false,
        new EntraAuthOptions(),
        AuthoritativeAboutSecrets: false,
        Running: null,
        ConfigurationEvidence.Unavailable);
}
