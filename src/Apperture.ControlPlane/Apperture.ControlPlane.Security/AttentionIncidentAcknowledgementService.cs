using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>What an acknowledgement request came to. Every value other than <see cref="Acknowledged"/> changed nothing.</summary>
public enum AttentionIncidentAcknowledgementOutcome
{
    /// <summary>The incident was active and unacknowledged: its acknowledgement and the one audit row committed.</summary>
    Acknowledged,

    /// <summary>The incident was active and already acknowledged. The first acknowledgement is kept.</summary>
    AlreadyAcknowledged,

    /// <summary>No such incident, or the incident belongs to another integration (or the integration is gone).</summary>
    NotFound,

    /// <summary>The incident is resolved: it can no longer be acknowledged.</summary>
    NotActive,

    /// <summary>The note is longer than <see cref="AttentionIncidentAcknowledgementBounds.NoteMaxLength"/> characters once trimmed.</summary>
    NoteTooLong,
}

public static class AttentionIncidentAcknowledgementBounds
{
    /// <summary>The width of the stored note column.</summary>
    public const int NoteMaxLength = 1024;
}

/// <summary>The stable <c>code</c> of an error body of the acknowledgement route.</summary>
public static class AttentionIncidentErrorCodes
{
    /// <summary>The incident is resolved (409).</summary>
    public const string NotActive = "attention_incident_not_active";

    /// <summary>The note is over the column width (400).</summary>
    public const string NoteTooLong = "attention_note_too_long";
}

/// <summary>
/// Acknowledges an active attention incident (SEC-2.1). The one writer of the acknowledgement columns: the tracker that opens and
/// resolves incidents has no write of them.
/// </summary>
public interface IAttentionIncidentAcknowledgementService
{
    /// <param name="integrationId">The integration the incident must belong to.</param>
    /// <param name="incidentId">The incident to acknowledge.</param>
    /// <param name="note">The operator's note: trimmed, blank is none, never recorded in audit.</param>
    /// <param name="reauthorize">
    /// The caller's authorization check (<c>integration.update</c> on the integration), run again by the service once the
    /// integration row is locked, so that an ownership move that committed after the caller's first check is seen. It throws to
    /// refuse. Required: no acknowledgement is made without it.
    /// </param>
    Task<AttentionIncidentAcknowledgementOutcome> AcknowledgeAsync(
        IntegrationId integrationId,
        string incidentId,
        string? note,
        Func<CancellationToken, ValueTask> reauthorize,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// SEC-2.1: acknowledging an attention incident is one audited unit of work. The three acknowledgement columns and the one
/// <c>attention_incident.acknowledged</c> row commit together or not at all.
/// </summary>
/// <remarks>
/// <para><b>Lock order</b>, after the unit's operation lock: the integration row (<c>UPDLOCK</c>, the row an ownership move and an
/// integration delete also take), the caller's re-run authorization against the integration as locked, then the incident row
/// (<c>UPDLOCK</c>) and its state. The observation worker resolves incidents without the integration row, so the incident lock is
/// what orders an acknowledgement against a resolution: whichever reaches the row first wins, the other reads the committed result.
/// A resolution that follows an acknowledgement writes only the columns it changes, so the acknowledgement is not lost.</para>
/// <para><b>State.</b> A resolved incident is refused (not active), including one that was acknowledged while it was active. An
/// active incident that is already acknowledged is a no-op: nothing is written or recorded, and the first acknowledger, time and
/// note are kept. Two acknowledgements racing each other serialize on the integration row, so one wins and the other is that no-op.</para>
/// <para><b>Actor.</b> The acknowledger is the unit's recorded actor, the same value the audit row carries. Nothing from the request
/// names who acknowledged. <b>Privacy.</b> The audit row says that a note was given and never what it said.</para>
/// <para>A lost commit acknowledgement is verified from the committed row and reported as the acknowledgement it was.</para>
/// </remarks>
public sealed class AttentionIncidentAcknowledgementService : IAttentionIncidentAcknowledgementService
{
    private readonly AppertureDbContext _db;
    private readonly IIntegrationRepository _integrations;
    private readonly IAuditedUnitOfWork _unit;
    private readonly TimeProvider _timeProvider;

    public AttentionIncidentAcknowledgementService(
        AppertureDbContext db,
        IIntegrationRepository integrations,
        IAuditedUnitOfWork unit,
        TimeProvider timeProvider)
    {
        _db = db;
        _integrations = integrations;
        _unit = unit;
        _timeProvider = timeProvider;
    }

    public async Task<AttentionIncidentAcknowledgementOutcome> AcknowledgeAsync(
        IntegrationId integrationId,
        string incidentId,
        string? note,
        Func<CancellationToken, ValueTask> reauthorize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reauthorize);

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > AttentionIncidentAcknowledgementBounds.NoteMaxLength })
            return AttentionIncidentAcknowledgementOutcome.NoteTooLong;

        return await _unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.AttentionIncidentAcknowledged, AuditActor.CurrentPrincipal),
            (transaction, ct) => AttemptAsync(transaction, integrationId, incidentId, trimmed, reauthorize, ct),
            // A verified commit is this request's acknowledgement having happened: the row it reads back is the one it wrote.
            (_, _) => Task.FromResult(AttentionIncidentAcknowledgementOutcome.Acknowledged),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<AttentionIncidentAcknowledgementOutcome> AttemptAsync(
        IAuditedTransaction transaction,
        IntegrationId integrationId,
        string incidentId,
        string? note,
        Func<CancellationToken, ValueTask> reauthorize,
        CancellationToken cancellationToken)
    {
        // Untracked and locking: the integration is fixed until the commit.
        var integration = await _integrations.GetByIdForUpdateAsync(integrationId, cancellationToken).ConfigureAwait(false);
        if (integration is null)
            return NoChange(transaction, AttentionIncidentAcknowledgementOutcome.NotFound);

        // The caller's check, run again now that the row cannot move: an ownership move that committed after the check made before
        // this call is seen here. It runs before the incident is read, so a refused caller learns nothing about the incident.
        await reauthorize(cancellationToken).ConfigureAwait(false);

        var incident = await LockIncidentAsync(integrationId, incidentId, cancellationToken).ConfigureAwait(false);
        if (incident is null)
            return NoChange(transaction, AttentionIncidentAcknowledgementOutcome.NotFound);
        if (!string.Equals(incident.Status, AttentionIncidentStatuses.Active, StringComparison.Ordinal))
            return NoChange(transaction, AttentionIncidentAcknowledgementOutcome.NotActive);
        if (incident.AcknowledgedAtUtc is not null)
            return NoChange(transaction, AttentionIncidentAcknowledgementOutcome.AlreadyAcknowledged);

        AttachForUpdate(incident);
        incident.AcknowledgedAtUtc = _timeProvider.GetUtcNow();
        incident.AcknowledgedBy = transaction.Actor.RecordedValue;
        incident.AcknowledgementNote = note;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.StageAsync(
            AuditEvents.AttentionIncidentAcknowledged(
                integrationId.Value,
                integration.DisplayName,
                incident.Id,
                incident.ConditionKey,
                incident.StartedAtUtc,
                noteProvided: note is not null,
                integration.ResourceContextId.Value),
            cancellationToken).ConfigureAwait(false);
        return AttentionIncidentAcknowledgementOutcome.Acknowledged;
    }

    private static AttentionIncidentAcknowledgementOutcome NoChange(
        IAuditedTransaction transaction,
        AttentionIncidentAcknowledgementOutcome outcome)
    {
        transaction.DeclareNoOp();
        return outcome;
    }

    /// <summary>
    /// The incident of this integration, locked to the end of the transaction on SQL Server (the update lock lives in the SQL text, so
    /// it is taken whether or not the row is tracked) and read as it is now, not as an earlier snapshot saw it. Untracked, so an
    /// execution-strategy retry never meets a stale entity; null when there is no such incident for this integration.
    /// </summary>
    private async Task<AttentionIncidentEntity?> LockIncidentAsync(
        IntegrationId integrationId,
        string incidentId,
        CancellationToken cancellationToken)
    {
        List<AttentionIncidentEntity> rows;
        if (_db.Database.IsRelational())
        {
            var integration = integrationId.Value;
            rows = await _db.AttentionIncidents
                .FromSqlInterpolated(
                    $"SELECT * FROM [AttentionIncidents] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {incidentId} AND [IntegrationId] = {integration}")
                .AsNoTracking()
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            rows = await _db.AttentionIncidents.AsNoTracking()
                .Where(e => e.Id == incidentId && e.IntegrationId == integrationId.Value)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        return rows.SingleOrDefault();
    }

    /// <summary>Starts tracking a row read untracked, as unchanged, so only the properties then set are written.</summary>
    private void AttachForUpdate(AttentionIncidentEntity row)
    {
        if (_db.AttentionIncidents.Local.FirstOrDefault(x => x.Id == row.Id) is { } tracked)
            _db.Entry(tracked).State = EntityState.Detached;
        _db.AttentionIncidents.Attach(row);
    }
}
