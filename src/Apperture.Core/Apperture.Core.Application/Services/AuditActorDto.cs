using Apperture.Core.Application.Audit;

namespace Apperture.Core.Application.Services;

/// <summary>
/// The actor of one audit row as readers see it (AUD-1 design gate §10.7). <see cref="Recorded"/> is the verbatim
/// <c>Actor</c>; <see cref="User"/> is the current identity of a <c>user</c> actor, composed server-side in one
/// <see cref="IUserReferenceResolver"/> batch per page; <see cref="UsernameAtEvent"/> is the event-time username evidence.
/// </summary>
/// <param name="Kind"><c>user</c>, <c>external</c>, <c>system</c>, <c>anonymous</c>, <c>worker</c>, <c>unresolved</c>, or the read-model value <c>unclassified</c>.</param>
/// <param name="Label">The product label for non-human kinds.</param>
/// <param name="Attribution">How the actor was established (§10.6), or <c>not_yet_classified</c>.</param>
public sealed record AuditActorDto(
    string Kind,
    UserReferenceDto? User,
    AuditExternalIdentityDto? External,
    string? Label,
    string Recorded,
    string? UsernameAtEvent,
    string Attribution,
    AuditAttributionIssueDto? Issue);

/// <summary>A verified external identity. The name hint is provided by the provider and is presentation only.</summary>
public sealed record AuditExternalIdentityDto(string Provider, string TenantId, string SubjectId, string? NameHint);

/// <summary>A known attribution defect of the row (§15.3).</summary>
/// <param name="Certainty"><c>definite</c> or <c>possible</c>.</param>
public sealed record AuditAttributionIssueDto(string Code, string Certainty);

/// <summary>The stored actor columns of one audit row.</summary>
public sealed record AuditStoredActor(
    string Actor,
    string? ClassificationState,
    string? ActorKind,
    string? ActorUserId,
    string? ActorUsername,
    string? ActorExternalProvider,
    string? ActorExternalTenantId,
    string? ActorExternalSubjectId,
    string? ActorAttribution,
    string? AttributionIssue,
    string? AttributionIssueCertainty);

/// <summary>Builds <see cref="AuditActorDto"/> from stored columns and the page's resolved user references.</summary>
public static class AuditActorProjection
{
    /// <summary>The label of an actor that no rule identified (IAM-9 R18, approved).</summary>
    public const string UnresolvedLabel = "Unrecognized actor";

    /// <summary>The label of a verified Microsoft Entra identity with no platform user.</summary>
    public const string ExternalEntraLabel = "Microsoft Entra identity";

    /// <param name="resolved">The page's resolved references; a <c>user</c> row whose id is missing degrades to <c>unresolved</c>.</param>
    public static AuditActorDto Project(AuditStoredActor row, IReadOnlyDictionary<string, UserReferenceDto> resolved)
    {
        if (row.ClassificationState != AuditClassificationStates.Complete || row.ActorKind is null || row.ActorAttribution is null)
        {
            return new AuditActorDto(
                AuditActorKinds.Unclassified, null, null, null, row.Actor, null, AuditAttributions.NotYetClassified, null);
        }

        var issue = row.AttributionIssue is { } code && row.AttributionIssueCertainty is { } certainty
            ? new AuditAttributionIssueDto(code, certainty)
            : null;

        switch (row.ActorKind)
        {
            case AuditActorKinds.User when row.ActorUserId is { } id && resolved.TryGetValue(id, out var user):
                return new AuditActorDto(AuditActorKinds.User, user, null, null, row.Actor, row.ActorUsername, row.ActorAttribution, issue);
            case AuditActorKinds.User:
                return new AuditActorDto(AuditActorKinds.Unresolved, null, null, UnresolvedLabel, row.Actor, row.ActorUsername, row.ActorAttribution, issue);
            case AuditActorKinds.External:
                return new AuditActorDto(
                    AuditActorKinds.External,
                    null,
                    new AuditExternalIdentityDto(row.ActorExternalProvider!, row.ActorExternalTenantId!, row.ActorExternalSubjectId!, NameHint: null),
                    ExternalEntraLabel,
                    row.Actor,
                    null,
                    row.ActorAttribution,
                    issue);
            case AuditActorKinds.Unresolved:
                return new AuditActorDto(AuditActorKinds.Unresolved, null, null, UnresolvedLabel, row.Actor, null, row.ActorAttribution, issue);
            default:
                return new AuditActorDto(
                    row.ActorKind, null, null, ActorLiterals.LabelOf(row.Actor) ?? "System", row.Actor, null, row.ActorAttribution, issue);
        }
    }
}
