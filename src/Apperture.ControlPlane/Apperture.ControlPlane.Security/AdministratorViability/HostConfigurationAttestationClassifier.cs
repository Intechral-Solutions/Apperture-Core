using Apperture.Core.Application.AdministratorViability;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>Why an attestation is, or is not, usable as evidence.</summary>
public enum AttestationAssessmentKind
{
    /// <summary>One live, readable, consistent attestation for this database.</summary>
    Fresh,

    /// <summary>No host has recorded anything.</summary>
    Missing,

    /// <summary>The newest record is older than the freshness window.</summary>
    Stale,

    /// <summary>The newest record was closed by a graceful stop.</summary>
    Stopped,

    /// <summary>The live record was written for another database.</summary>
    DatabaseMismatch,

    /// <summary>The live record has a schema version, or a source, this reader does not know.</summary>
    UnknownSchema,

    /// <summary>The live record is of a known version and cannot be read.</summary>
    Unreadable,

    /// <summary>More than one host has a live record: which one is authoritative cannot be established.</summary>
    Conflict,

    /// <summary>A record is dated after the database's own clock: its age cannot be judged.</summary>
    ClockInconsistent,
}

/// <summary>
/// The verdict on the attestation table's contents.
/// </summary>
/// <param name="Kind">Why.</param>
/// <param name="Reading">The reading the attestation describes, only when <paramref name="Kind"/> is <see cref="AttestationAssessmentKind.Fresh"/>.</param>
/// <param name="Reason">What an operator can act on, in product wording. It names no host, database, tenant or value.</param>
public sealed record AttestationAssessment(AttestationAssessmentKind Kind, DeploymentAuthenticationReading? Reading, string? Reason)
{
    /// <summary>True when the attestation is fresh and consistent.</summary>
    public bool IsAttested => Kind == AttestationAssessmentKind.Fresh;

    /// <summary>The class of evidence the assessment is: attested, or unavailable. It is never "not required": that is an operation's finding.</summary>
    public ConfigurationEvidence Evidence => IsAttested ? ConfigurationEvidence.Attested : ConfigurationEvidence.Unavailable;
}

/// <summary>
/// Decides, from what the attestation table holds and the database's own time, whether a Management Host's reading may stand in for
/// the configuration of the installation. Pure: it reads no clock of its own, so every boundary is testable exactly.
/// </summary>
/// <remarks>
/// <para>A row is <b>live</b> when it is not stopped and its age (the database's time minus its last refresh) is not greater than
/// the window. Exactly one live row is needed. None is missing, stale or stopped evidence; two or more is a conflict (RC1 supports one
/// Management Host); a live row for another database, of a version or source this reader does not know, or that cannot be read, is
/// not evidence. A row that is not live is ignored when a live one exists, so a host that moved leaves nothing behind that is
/// believed. A row dated after the database's clock is never believed.</para>
/// <para>Nothing here validates the configuration the host reported. That is the evaluation's job; a fresh attestation of a
/// configuration that does not work is still a fresh attestation, and the evaluation reads what it says.</para>
/// </remarks>
public static class HostConfigurationAttestationClassifier
{
    /// <summary>Assesses <paramref name="snapshot"/> against the freshness <paramref name="window"/>.</summary>
    public static AttestationAssessment Assess(HostConfigurationAttestationSnapshot snapshot, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Rows.Count == 0)
            return Unavailable(AttestationAssessmentKind.Missing, "The Management Host has not recorded its sign-in configuration for this database. Start the Management Host, then run the command again.");

        var live = new List<HostConfigurationAttestationRow>();
        foreach (var row in snapshot.Rows)
        {
            if (row.StoppedUtc is not null)
                continue;

            var age = snapshot.DatabaseNowUtc - row.RefreshedUtc;
            if (age < TimeSpan.Zero)
                return Unavailable(AttestationAssessmentKind.ClockInconsistent, "The Management Host's record of its sign-in configuration is dated later than the database's own clock, so its age cannot be judged.");

            if (age <= window)
                live.Add(row);
        }

        if (live.Count > 1)
            return Unavailable(AttestationAssessmentKind.Conflict, "More than one Management Host has recorded its sign-in configuration recently, so which one is authoritative cannot be established. Stop the extra host, wait a few minutes, then run the command again.");

        if (live.Count == 0)
        {
            // Nothing is live: say whether the newest record was closed by a stop or simply aged out.
            var newest = snapshot.Rows.OrderByDescending(r => r.RefreshedUtc).First();
            return newest.StoppedUtc is not null
                ? Unavailable(AttestationAssessmentKind.Stopped, "The Management Host has stopped. Start it, then run the command again.")
                : Unavailable(AttestationAssessmentKind.Stale, "The Management Host's record of its sign-in configuration is out of date. Make sure the Management Host is running, then run the command again.");
        }

        var only = live[0];
        if (!string.Equals(only.DatabaseFingerprint, snapshot.DatabaseFingerprint, StringComparison.Ordinal))
            return Unavailable(AttestationAssessmentKind.DatabaseMismatch, "The Management Host's record of its sign-in configuration was written for a different database.");

        if (!string.Equals(only.Source, HostConfigurationAttestationSource.ManagementHost, StringComparison.Ordinal)
            || only.SchemaVersion != AttestedAuthenticationPayload.SchemaVersion)
        {
            return Unavailable(AttestationAssessmentKind.UnknownSchema, "The Management Host recorded its sign-in configuration in a form this command does not understand. Use the version of the command that matches the Management Host.");
        }

        if (!AttestedAuthenticationPayload.TryParse(only.Payload, out var payload) || payload is null)
            return Unavailable(AttestationAssessmentKind.Unreadable, "The Management Host's record of its sign-in configuration cannot be read.");

        return new AttestationAssessment(AttestationAssessmentKind.Fresh, payload.ToReading(), Reason: null);
    }

    private static AttestationAssessment Unavailable(AttestationAssessmentKind kind, string reason) => new(kind, Reading: null, reason);
}
