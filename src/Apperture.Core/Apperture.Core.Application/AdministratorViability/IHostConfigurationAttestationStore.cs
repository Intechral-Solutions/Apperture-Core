namespace Apperture.Core.Application.AdministratorViability;

/// <summary>
/// One Management Host's recorded reading of its own authentication configuration, as it is stored. Every time that decides
/// freshness comes from the database's clock: the host's clock never does.
/// </summary>
/// <param name="HostId">The stable identity of the host (its machine, content root and application), not of one process.</param>
/// <param name="Source">Who wrote the row. Only <see cref="HostConfigurationAttestationSource.ManagementHost"/> is known.</param>
/// <param name="SchemaVersion">The version of the shape of <paramref name="Payload"/>. A version the reader does not know is not read.</param>
/// <param name="ProcessInstanceId">The process that owns the row. A newer process of the same host replaces the row.</param>
/// <param name="ProcessStartedUtc">When the host says its process started. Informational: no decision depends on it.</param>
/// <param name="ClaimedUtc">The database's time when the owning process took the row.</param>
/// <param name="RefreshedUtc">The database's time of the owning process's latest refresh.</param>
/// <param name="StoppedUtc">The database's time of the owning process's graceful stop, or null while it has not stopped.</param>
/// <param name="DatabaseFingerprint">The database the host was connected to when it wrote the row (server and database).</param>
/// <param name="Payload">The readings, as a document of <paramref name="SchemaVersion"/>. It holds no secret.</param>
public sealed record HostConfigurationAttestationRow(
    string HostId,
    string Source,
    int SchemaVersion,
    string ProcessInstanceId,
    DateTimeOffset ProcessStartedUtc,
    DateTime ClaimedUtc,
    DateTime RefreshedUtc,
    DateTime? StoppedUtc,
    string DatabaseFingerprint,
    string Payload);

/// <summary>The values of <see cref="HostConfigurationAttestationRow.Source"/> a reader knows.</summary>
public static class HostConfigurationAttestationSource
{
    /// <summary>The Management Host wrote the row about itself.</summary>
    public const string ManagementHost = "management_host";
}

/// <summary>What a host writes: who it is and what it reads about itself. The store adds the times and the database binding.</summary>
/// <param name="HostId">The stable identity of the host.</param>
/// <param name="ProcessInstanceId">The identity of this process.</param>
/// <param name="ProcessStartedUtc">When this process started, by the host's clock (informational).</param>
/// <param name="SchemaVersion">The version of <paramref name="Payload"/>.</param>
/// <param name="Payload">The readings. It must hold no secret.</param>
public sealed record HostConfigurationAttestationWrite(
    string HostId,
    string ProcessInstanceId,
    DateTimeOffset ProcessStartedUtc,
    int SchemaVersion,
    string Payload);

/// <summary>What a read of the attestation table found, with the database's own time and binding taken at the same moment.</summary>
/// <param name="Rows">Every row, live or not. Deciding which are live is the reader's job, by the database time below.</param>
/// <param name="DatabaseNowUtc">The database's time, read after the rows so that a refresh in between can only make a row look older.</param>
/// <param name="DatabaseFingerprint">The fingerprint of the database this read was made against, computed the way a writer computes it.</param>
public sealed record HostConfigurationAttestationSnapshot(
    IReadOnlyList<HostConfigurationAttestationRow> Rows,
    DateTime DatabaseNowUtc,
    string DatabaseFingerprint);

/// <summary>What a refresh by a process found.</summary>
public enum HostAttestationRefreshOutcome
{
    /// <summary>The process still owns its row and the refresh was recorded.</summary>
    Refreshed,

    /// <summary>A newer process of the same host owns the row, or this process has already stopped. The caller must stop publishing.</summary>
    Superseded,

    /// <summary>The host has no row (the table was emptied, or the database was replaced). The caller claims it again.</summary>
    Missing,
}

/// <summary>
/// The database side of the Management Host's configuration attestation. The host writes its row at start, refreshes it, and marks
/// it stopped on a graceful stop; the emergency command line reads it. Nothing in the table is configuration: it is a dated
/// observation, and the only judge of its age is the database's clock.
/// </summary>
public interface IHostConfigurationAttestationStore
{
    /// <summary>
    /// Takes the host's row for <paramref name="write"/>'s process, replacing whatever an earlier process of the same host wrote, and
    /// stamps it with the database's time and database binding. The row is live from this moment.
    /// </summary>
    ValueTask ClaimAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the row if, and only if, <paramref name="write"/>'s process still owns it and has not stopped. A superseded process
    /// never overwrites the process that replaced it.
    /// </summary>
    ValueTask<HostAttestationRefreshOutcome> RefreshAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the row stopped if, and only if, the process still owns it. A stopped row is never evidence. Returns whether the mark
    /// was made; a superseded process leaves the replacing process's row alone.
    /// </summary>
    ValueTask<bool> MarkStoppedAsync(string hostId, string processInstanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads every row with the database's time and binding. When the caller has an open transaction on the same context the read
    /// joins it, so a reader inside an audited unit reads under the unit's own isolation.
    /// </summary>
    ValueTask<HostConfigurationAttestationSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}
