using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer.Audit;

/// <summary>
/// Loads the identity data that can bear on one actor value, so an <c>append</c> row is classified at insert by exactly the
/// rules of the startup step without loading every user (§14).
/// </summary>
/// <remarks>
/// <see cref="HistoricalActorClassifier"/> reads only: whether the value is a user id; the ledger owners of the value; the
/// Entra link owners of the value; and, for the single candidate, the ledger and link owners of that candidate's id. The
/// queries below return a superset of those rows (the database compares case-insensitively and ignores trailing spaces),
/// and the classifier applies its own exact comparisons, so the result equals classification against the whole tables.
/// </remarks>
internal static class AuditActorEvidence
{
    public static async Task<HistoricalActorClassifier> LoadForValueAsync(AppertureDbContext db, string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value)
            || ActorLiterals.IsNonHumanLiteral(value)
            || string.Equals(value, AuditRowClassifier.UnknownPlaceholder, StringComparison.Ordinal))
        {
            return new HistoricalActorClassifier([], [], []);
        }

        var userIds = await db.Users.AsNoTracking()
            .Where(u => u.Id == value)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var ledger = await db.UsernameLedger.AsNoTracking()
            .Where(l => l.Username == value)
            .Select(l => new { l.Username, l.UserId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var links = await db.IdentityLinks.AsNoTracking()
            .Where(l => l.Source == (int)IdentitySource.Entra && l.ExternalId == value)
            .Select(l => new { l.Id, l.ExternalId, l.UserId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var candidates = userIds
            .Concat(ledger.Select(l => l.UserId))
            .Concat(links.Select(l => l.UserId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (candidates.Count > 0)
        {
            ledger.AddRange(await db.UsernameLedger.AsNoTracking()
                .Where(l => candidates.Contains(l.Username))
                .Select(l => new { l.Username, l.UserId })
                .ToListAsync(cancellationToken).ConfigureAwait(false));
            links.AddRange(await db.IdentityLinks.AsNoTracking()
                .Where(l => l.Source == (int)IdentitySource.Entra && candidates.Contains(l.ExternalId))
                .Select(l => new { l.Id, l.ExternalId, l.UserId })
                .ToListAsync(cancellationToken).ConfigureAwait(false));
        }

        // The classifier counts links per object id, so each link appears once however many queries returned it.
        return new HistoricalActorClassifier(
            userIds,
            ledger.DistinctBy(l => (l.Username, l.UserId)).Select(l => (l.Username, l.UserId)),
            links.DistinctBy(l => l.Id).Select(l => (l.ExternalId, l.UserId)));
    }
}

/// <summary>Writes a classification set onto an entity, all at once.</summary>
internal static class AuditClassificationColumns
{
    public static void Apply(AuditLogEntity entity, AuditClassificationSet set)
    {
        entity.ClassificationState = AuditClassificationStates.Complete;
        entity.ActorKind = set.ActorKind;
        entity.ActorUserId = set.ActorUserId;
        entity.ActorUsername = set.ActorUsername;
        entity.ActorExternalProvider = set.ActorExternalProvider;
        entity.ActorExternalTenantId = set.ActorExternalTenantId;
        entity.ActorExternalSubjectId = set.ActorExternalSubjectId;
        entity.ActorAttribution = set.ActorAttribution;
        entity.AttributionIssue = set.AttributionIssue;
        entity.AttributionIssueCertainty = set.AttributionIssueCertainty;
        entity.TargetType = set.TargetType;
        entity.TargetId = set.TargetId;
        entity.Category = set.Category;
        entity.Severity = set.Severity;
        entity.Visibility = set.Visibility;
        entity.Basis = set.Basis;
        entity.OutcomeCode = set.OutcomeCode;
        entity.OutcomeProvenance = set.OutcomeProvenance;
    }
}
