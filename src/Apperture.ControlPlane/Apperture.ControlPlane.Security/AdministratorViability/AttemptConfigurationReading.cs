using Apperture.Core.Application.AdministratorViability;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>
/// The one reading of the deployment's authentication configuration that one protected mutation attempt uses (IAM-10.2b). It belongs
/// to one <see cref="AdministratorViabilityScope"/>, so to one hold of the administrator-set gate and one transaction: a rerun of the
/// unit enters the gate again, gets a new scope and reads again. Nothing else keeps a reading, and nothing here is shared.
/// </summary>
/// <remarks>
/// <para><b>Why one.</b> The state before and the state after a change were each read through the attestation, so one attempt could
/// see the evidence fresh and then stale, or unavailable and then attested, and judge its two states under two different readings.
/// Pinning the reading gives the attempt one answer. Only the deployment reading is pinned: the stored facts and the persisted
/// registration are read again after the change, because the change is what the second read exists to see.</para>
/// <para><b>When it is read.</b> On the first read the scope makes, after the gate is held, inside the unit's transaction and before
/// the mutation. A wait for the gate therefore never ages the evidence, and an attempt that reads nothing reads no evidence.</para>
/// <para><b>What holding it cannot do.</b> A reading from the attestation may be relied on only while the attestation would still be
/// fresh. <see cref="HasExpired"/> reports the moment that stops being true, so a slow attempt cannot use evidence past the window it
/// was given, and the attempt refuses instead of reading again. The elapsed time starts before the store is asked, which makes the
/// limit earlier than the database's clock would make it, never later. A reader that is the host itself reads its own options, which
/// have no window. A failed read is not retried inside the attempt, because a second read would be a second answer.</para>
/// </remarks>
public sealed class AttemptConfigurationReading
{
    /// <summary>What an operator is told when the evidence ran out during the attempt. It names no host, database, tenant or value.</summary>
    public const string ExpiredReason =
        "The Management Host's record of its sign-in configuration went out of date while the change was being checked. Make sure the Management Host is running, then run the command again.";

    private readonly TimeProvider _time;
    private DeploymentAuthenticationReading? _reading;
    private long _startedAt;
    private bool _begun;

    /// <param name="time">The monotonic clock the elapsed time is measured with.</param>
    public AttemptConfigurationReading(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>
    /// The attempt's reading: read from <paramref name="deployment"/> the first time and returned unchanged after that.
    /// </summary>
    /// <exception cref="InvalidOperationException">The first read failed. The attempt has no reading, and it never gets a second one.</exception>
    public async ValueTask<DeploymentAuthenticationReading> GetAsync(
        IDeploymentAuthenticationConfiguration deployment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        if (_reading is { } pinned)
            return pinned;

        if (_begun)
        {
            throw new InvalidOperationException(
                "The attempt's configuration reading failed and is not read again. A new attempt of the unit reads it again.");
        }

        _begun = true;
        _startedAt = _time.GetTimestamp();
        var reading = await deployment.ReadAsync(cancellationToken).ConfigureAwait(false);
        _reading = reading;
        return reading;
    }

    /// <summary>
    /// True when the attempt's reading came from the attestation and can no longer be relied on: more time has passed since the read
    /// began than the attestation had left. An attested reading that does not say how long it has left is treated as having run out.
    /// False when nothing has been read yet, when the reader is the host itself, and when the reading was already not evidence (a
    /// reading of <see cref="ConfigurationEvidence.Unavailable"/> is not trusted from the start).
    /// </summary>
    public bool HasExpired
    {
        get
        {
            if (_reading is not { Evidence: ConfigurationEvidence.Attested } reading)
                return false;

            return reading.EvidenceRemaining is not { } remaining
                || _time.GetElapsedTime(_startedAt) > remaining;
        }
    }
}
