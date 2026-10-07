using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Shared.Kernel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using AuditEvent = Apperture.Core.Application.Audit.AuditEvent;

namespace Apperture.Persistence.SqlServer.Audit;

/// <summary>An actor as the recorder resolved it, with whether its session was established through Recovery Access.</summary>
internal readonly record struct ResolvedActor(AuditActor Actor, bool ViaRecoveryAccess);

/// <summary>Builds complete recorder rows (§11.8 rule 1) and recognises the audit unique-key violations.</summary>
internal static class AuditRecorderRows
{
    /// <summary>
    /// Resolves <see cref="AuditActor.CurrentPrincipal"/> from the authenticated context. A person is a user id (the session
    /// identity since IAM-9.2); nothing here trusts a username claim. A session established through Recovery Access marks
    /// the rows it writes <c>viaRecoveryAccess</c>: that comes from the principal, never from a caller.
    /// </summary>
    public static async Task<ResolvedActor> ResolveAsync(AuditActor actor, IPrincipalContext? principals, CancellationToken cancellationToken)
    {
        if (!actor.IsCurrentPrincipal)
            return new ResolvedActor(actor, ViaRecoveryAccess: false);
        if (principals is null)
            throw new AuditInvariantException("An audited operation records the current principal, but no principal context is available here.");

        var current = await principals.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new AuditInvariantException("An audited operation records the current principal, but no one is authenticated.");
        return current.Kind switch
        {
            ActorKind.Human => new ResolvedActor(
                AuditActor.ForUser(current.Id.Value),
                current is IRecoveryAccessSession { IsRecoveryAccessSession: true }),
            ActorKind.System => new ResolvedActor(AuditActor.System(), ViaRecoveryAccess: false),
            _ => throw new AuditInvariantException($"An audited operation cannot record a {current.Kind} principal."),
        };
    }

    /// <summary>
    /// Reads the actor's username in the transaction current on <paramref name="db"/> (§10.3): the account's username as
    /// this transaction sees it, including this transaction's own rename of it. Null for non-user actors.
    /// </summary>
    public static async Task<string?> ReadUsernameAsync(AppertureDbContext db, AuditActor actor, CancellationToken cancellationToken)
    {
        if (actor.Kind != AuditActorKinds.User)
            return null;
        var username = await db.Users.AsNoTracking()
            .Where(u => u.Id == actor.UserId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return username
            ?? throw new AuditInvariantException($"The acting user {actor.UserId} does not exist, so the operation cannot be attributed.");
    }

    /// <summary>A complete recorder row for <paramref name="auditEvent"/>.</summary>
    public static AuditLogEntity Build(
        AuditEvent auditEvent,
        AuditActor actor,
        string? actorUsername,
        string eventId,
        string operationEventId,
        DateTimeOffset at,
        bool viaRecoveryAccess = false)
    {
        var definition = auditEvent.Definition;
        var details = definition.Details!.Serialize(definition.Action, definition.WriteClass, auditEvent.Fields, viaRecoveryAccess);
        return new AuditLogEntity
        {
            At = at,
            Actor = actor.RecordedValue,
            Action = definition.Action,
            Details = details.Json,
            Outcome = auditEvent.OutcomeCode,
            ModuleId = auditEvent.Related.ModuleId,
            ScheduleId = auditEvent.Related.ScheduleId,
            RunId = auditEvent.Related.RunId,
            ResourceContextId = auditEvent.Related.ResourceContextId,
            CorrelationId = auditEvent.Related.CorrelationId,
            EventId = eventId,
            OperationEventId = operationEventId,
            Producer = AuditProducers.Recorder,
            DetailsFormat = AuditDetailsFormats.Json1,
            ClassificationState = AuditClassificationStates.Complete,
            ActorKind = actor.Kind,
            ActorUserId = actor.UserId,
            ActorUsername = actorUsername,
            ActorExternalProvider = actor.External?.Provider,
            ActorExternalTenantId = actor.External?.TenantId,
            ActorExternalSubjectId = actor.External?.SubjectId,
            ActorAttribution = AuditAttributions.Recorded,
            AttributionIssue = null,
            AttributionIssueCertainty = null,
            TargetType = auditEvent.Target?.Type,
            TargetId = auditEvent.Target?.Id,
            Category = definition.Category,
            Severity = definition.Severity,
            Visibility = definition.RoutineOutcomes.Contains(auditEvent.OutcomeCode) ? AuditVisibilities.Routine : AuditVisibilities.Default,
            Basis = definition.Basis,
            OutcomeCode = auditEvent.OutcomeCode,
            OutcomeProvenance = AuditOutcomeProvenances.Recorded,
        };
    }

    /// <summary>True when <paramref name="ex"/> is a violation of the <c>AuditLogs.EventId</c> unique index.</summary>
    public static bool IsEventIdViolation(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql
                && sql.Errors.Cast<SqlError>().Any(e =>
                    e.Number is 2601 or 2627 && e.Message.Contains(AuditLogSchema.EventIdIndex, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }
}
