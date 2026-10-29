namespace Apperture.Persistence.SqlServer;

/// <summary>
/// One Management Host's dated reading of its own authentication configuration (one row per host, keyed by the host's stable id).
/// Operational evidence for the emergency command line, which cannot observe the host: not configuration, and not an authority over
/// it. Every time in it is the database's clock, so the age of a row never depends on a host's clock.
/// </summary>
/// <remarks>
/// The table is written only by <see cref="SqlHostConfigurationAttestationStore"/>, with SQL that stamps the times itself, so the
/// entity is mapped for the model and the schema and is never tracked or saved through the context.
/// </remarks>
public sealed class HostConfigurationAttestationEntity
{
    public string HostId { get; set; } = "";

    public string Source { get; set; } = "";

    public int SchemaVersion { get; set; }

    public string ProcessInstanceId { get; set; } = "";

    public DateTimeOffset ProcessStartedUtc { get; set; }

    public DateTime ClaimedUtc { get; set; }

    public DateTime RefreshedUtc { get; set; }

    public DateTime? StoppedUtc { get; set; }

    public string DatabaseFingerprint { get; set; } = "";

    public string Payload { get; set; } = "";
}
