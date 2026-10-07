using System.Text.Json;
using Apperture.Core.Application.Services;

namespace Apperture.Core.Application.Audit;

/// <summary>
/// The whole classification set of one row (§11.8). Intentional nulls are part of it: once a row is complete, no column of
/// the set changes, including a null one.
/// </summary>
public sealed record AuditClassificationSet(
    string ActorKind,
    string? ActorUserId,
    string? ActorUsername,
    string? ActorExternalProvider,
    string? ActorExternalTenantId,
    string? ActorExternalSubjectId,
    string ActorAttribution,
    string? AttributionIssue,
    string? AttributionIssueCertainty,
    string? TargetType,
    string? TargetId,
    string Category,
    string Severity,
    string Visibility,
    string Basis,
    string OutcomeCode,
    string OutcomeProvenance);

/// <summary>The evidence columns of a row the recorder did not write.</summary>
/// <param name="Producer"><c>append</c>, or null for a row written before AUD-1.1.</param>
public sealed record AuditLegacyRow(string Actor, string Action, string? Outcome, string? Details, string? Producer);

/// <summary>
/// Classifies a row the recorder did not write: an <c>append</c> row at insert, or a legacy row at startup
/// (§14, §15). Both paths use this one function, so the same rules apply wherever a row is classified.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Actor (§15.2): IAM-9 R17 through <see cref="HistoricalActorClassifier"/>, unchanged, plus the audit-only
/// <c>unknown</c> protection. Never <c>external</c>, never an invented username: <c>ActorUsername</c> is set only to a
/// recorded sign-in name matched through the ledger.</item>
/// <item>Defects (§15.3): only for rows not written by the recorder, from the catalog's verified entries.</item>
/// <item>Category, severity, basis (§15.4): from the catalog; an unknown action is <c>other</c>, <c>standard</c>,
/// <c>unspecified</c>.</item>
/// <item>Outcome (§11.5, AC-4): <c>recorded</c> provenance for a row with a producer or an action first written by code
/// containing the Epic 37 migration; otherwise <c>indeterminate</c>, normalized only for an era-invariant action. The raw
/// value is never changed.</item>
/// <item>Visibility (§11.4): <c>routine</c> only for a routine-eligible action with a declared routine outcome.</item>
/// <item>Target (§12.6 P-5): only from the typed JSON families, deserialized exactly; otherwise null.</item>
/// </list>
/// </remarks>
public static class AuditRowClassifier
{
    /// <summary>The placeholder some writers recorded when no actor resolved. Never looked up in the ledger (§15.2).</summary>
    public const string UnknownPlaceholder = "unknown";

    public static AuditClassificationSet Classify(AuditLegacyRow row, HistoricalActorClassifier actors, AuditActionCatalog catalog)
    {
        if (row.Producer == AuditProducers.Recorder)
            throw new ArgumentException("Recorder rows are inserted complete and are never classified afterwards.", nameof(row));

        var definition = catalog.Find(row.Action);
        var (kind, userId, username, attribution) = ClassifyActor(row.Actor, actors);
        var (issue, certainty) = FindDefect(definition, row.Actor);

        var provenance = row.Producer is not null || definition?.IntroducedAfterEpic37Migration == true
            ? AuditOutcomeProvenances.Recorded
            : AuditOutcomeProvenances.Indeterminate;
        var outcome = provenance == AuditOutcomeProvenances.Recorded || definition?.EraInvariant == true
            ? AuditActionCatalog.MapRecordedOutcome(definition, row.Outcome)
            : AuditOutcomeCodes.Unknown;

        var visibility = definition is not null && definition.RoutineOutcomes.Contains(outcome)
            ? AuditVisibilities.Routine
            : AuditVisibilities.Default;

        var target = definition is null ? null : LegacyTarget(definition, row.Details);

        return new AuditClassificationSet(
            kind,
            userId,
            username,
            ActorExternalProvider: null,
            ActorExternalTenantId: null,
            ActorExternalSubjectId: null,
            attribution,
            issue,
            certainty,
            target?.Type,
            target?.Id,
            definition?.Category ?? AuditCategories.Other,
            definition?.Severity ?? AuditSeverities.Standard,
            visibility,
            definition?.Basis ?? AuditBases.Unspecified,
            outcome,
            provenance);
    }

    private static (string Kind, string? UserId, string? Username, string Attribution) ClassifyActor(
        string actor,
        HistoricalActorClassifier actors)
    {
        // Audit-only protection: the placeholder never reaches the ledger, so a user named "unknown" is never credited.
        if (string.Equals(actor, UnknownPlaceholder, StringComparison.Ordinal))
            return (AuditActorKinds.Unresolved, null, null, AuditAttributions.Unresolved);

        var result = actors.ClassifyReportingRule(actor);
        return result.Classification.Category switch
        {
            HistoricalActorCategory.Literal =>
                (ActorLiterals.AuditKindOf(actor)!, null, null, AuditAttributions.RecordedLiteral),
            HistoricalActorCategory.AlreadyUserId =>
                (AuditActorKinds.User, actor, null, AuditAttributions.RecordedUserId),
            HistoricalActorCategory.Resolved when result.Rule == HistoricalActorRule.Username =>
                (AuditActorKinds.User, result.Classification.UserId, actor, AuditAttributions.InferredUsername),
            HistoricalActorCategory.Resolved =>
                (AuditActorKinds.User, result.Classification.UserId, null, AuditAttributions.InferredEntraObjectId),
            HistoricalActorCategory.Ambiguous =>
                (AuditActorKinds.Unresolved, null, null, AuditAttributions.Ambiguous),
            _ =>
                (AuditActorKinds.Unresolved, null, null, AuditAttributions.Unresolved),
        };
    }

    private static (string? Code, string? Certainty) FindDefect(AuditActionDefinition? definition, string actor)
    {
        var defect = definition?.Defects.FirstOrDefault(d => d.AppliesTo(actor));
        return (defect?.Code, defect?.Certainty);
    }

    private static readonly HashSet<string> ConnectionProperties = new(StringComparer.Ordinal)
    {
        "resourceType", "resourceId", "displayName", "connectorDefinitionId",
        "nameChanged", "configFieldsChanged", "secretFieldsRotated", "verificationStatus",
    };

    private static readonly HashSet<string> ConnectionToolProperties = new(StringComparer.Ordinal)
    {
        "resourceType", "resourceId", "displayName", "connectorDefinitionId",
        "tool", "operation", "toolOutcome", "resultCount", "truncated",
    };

    private static readonly HashSet<string> MoveProperties = new(StringComparer.Ordinal)
    {
        "resourceType", "resourceId", "displayName", "sourceResourceContextId", "targetResourceContextId", "movedScheduleIds",
    };

    private static readonly HashSet<string> MoveRejectedProperties = new(StringComparer.Ordinal)
    {
        "resourceType", "resourceId", "displayName", "sourceResourceContextId", "targetResourceContextId", "blockerCodes",
    };

    /// <summary>
    /// P-5: a target from a typed JSON family, only when the details are exactly one of its documents: an object whose
    /// properties are all declared by the record, with every required property of the right type. Anything else (free
    /// text, a <c>Resource=</c> suffix, an unknown property) gives no target.
    /// </summary>
    internal static AuditTarget? LegacyTarget(AuditActionDefinition definition, string? details)
    {
        if (definition.LegacyTarget == AuditLegacyTargetSource.None || string.IsNullOrWhiteSpace(details))
            return null;

        try
        {
            using var document = JsonDocument.Parse(details);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!TryString(root, "resourceType", out var resourceType) || !TryString(root, "resourceId", out var resourceId)
                || resourceId.Length > AuditTarget.MaxIdLength)
                return null;
            if (!root.TryGetProperty("displayName", out var displayName)
                || displayName.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                return null;

            if (definition.LegacyTarget == AuditLegacyTargetSource.ConnectionJson)
            {
                if (resourceType != "Connection" || !TryString(root, "connectorDefinitionId", out _))
                    return null;
                var isTool = root.TryGetProperty("tool", out _);
                var declared = isTool ? ConnectionToolProperties : ConnectionProperties;
                if (!OnlyDeclared(root, declared))
                    return null;
                if (isTool && (!TryString(root, "tool", out _) || !TryString(root, "operation", out _) || !TryString(root, "toolOutcome", out _)))
                    return null;
                return new AuditTarget(AuditTargetTypes.Connection, resourceId);
            }

            if (!TryString(root, "sourceResourceContextId", out _) || !TryString(root, "targetResourceContextId", out _))
                return null;
            var rejected = root.TryGetProperty("blockerCodes", out var blockers);
            if (rejected && blockers.ValueKind != JsonValueKind.Array)
                return null;
            if (!OnlyDeclared(root, rejected ? MoveRejectedProperties : MoveProperties))
                return null;

            return resourceType switch
            {
                "Integration" => new AuditTarget(AuditTargetTypes.Integration, resourceId),
                "Connection" => new AuditTarget(AuditTargetTypes.Connection, resourceId),
                "Schedule" => new AuditTarget(AuditTargetTypes.Schedule, resourceId),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryString(JsonElement root, string name, out string value)
    {
        value = "";
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        value = element.GetString()!;
        return value.Length > 0;
    }

    private static bool OnlyDeclared(JsonElement root, HashSet<string> declared) =>
        root.EnumerateObject().All(p => declared.Contains(p.Name));
}
