namespace Apperture.Core.Application.Audit;

/// <summary>A known attribution defect of rows of one action that the recorder did not write (§15.3).</summary>
/// <param name="WhenActorEquals">The defect applies only to rows recording exactly this actor (ordinal); null means any actor.</param>
/// <param name="UnlessActorEquals">The defect does not apply to rows recording exactly this actor (ordinal).</param>
public sealed record AuditAttributionDefect(string Code, string Certainty, string? WhenActorEquals = null, string? UnlessActorEquals = null)
{
    public bool AppliesTo(string actor) =>
        (WhenActorEquals is null || string.Equals(WhenActorEquals, actor, StringComparison.Ordinal))
        && (UnlessActorEquals is null || !string.Equals(UnlessActorEquals, actor, StringComparison.Ordinal));
}

/// <summary>Where a legacy row of an action may take its target from (§12.6 P-5).</summary>
public enum AuditLegacyTargetSource
{
    None,

    /// <summary>The details are a <c>ConnectionAuditDetails</c> or <c>ConnectionToolAuditDetails</c> JSON document.</summary>
    ConnectionJson,

    /// <summary>The details are a <c>ResourceMoveAuditDetails</c> or <c>ResourceMoveRejectedAuditDetails</c> JSON document.</summary>
    ResourceMoveJson,
}

/// <summary>
/// One declared action (§11.7): its subject area, severity, target, basis, write class, routine eligibility, detail schema,
/// and the provenance facts that decide how a legacy row's outcome is read (§11.5).
/// </summary>
public sealed record AuditActionDefinition
{
    public required string Action { get; init; }
    public required string Category { get; init; }
    public required string Severity { get; init; }

    /// <summary>The primary target type, or null for an action without one.</summary>
    public string? TargetType { get; init; }

    public required string Basis { get; init; }
    public required AuditWriteClass WriteClass { get; init; }

    /// <summary>The normalized outcomes for which a row is <c>routine</c> (§11.4). Empty: never routine.</summary>
    public IReadOnlySet<string> RoutineOutcomes { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The structured detail schema of the action's typed producer; null while the action is written through the append bridge.</summary>
    public AuditDetailSchema? Details { get; init; }

    /// <summary>The commit that first wrote this action name (verified against git history, I-6).</summary>
    public required string IntroducedIn { get; init; }

    /// <summary>
    /// True when <see cref="IntroducedIn"/> contains <c>baa2f763</c>, the commit that made the Epic 37 migration effective.
    /// Every binary able to write the action then applied that migration first, so no row of it predates the backfill.
    /// </summary>
    public required bool IntroducedAfterEpic37Migration { get; init; }

    /// <summary>
    /// For an action older than Epic 37: its writer passed the same outcome value with the same meaning before Epic 37 (as
    /// the first segment of Details, which the migration copied) and after (in the column), so either provenance reads the
    /// same. False otherwise.
    /// </summary>
    public bool EraInvariant { get; init; }

    /// <summary>
    /// Action-specific raw outcome mappings (ordinal), checked before the generic ones. A value mapped to
    /// <see cref="AuditOutcomeCodes.Unknown"/> here is deliberately not normalized.
    /// </summary>
    public IReadOnlyDictionary<string, string> OutcomeMap { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Known attribution defects of rows not written by the recorder.</summary>
    public IReadOnlyList<AuditAttributionDefect> Defects { get; init; } = [];

    /// <summary>The typed JSON family that may supply a legacy row's target (P-5).</summary>
    public AuditLegacyTargetSource LegacyTarget { get; init; } = AuditLegacyTargetSource.None;

    /// <summary>Where the action is written today, for the handback and the pinned inventory (not runtime-visible).</summary>
    public string? Writer { get; init; }

    /// <summary>
    /// True when no production code writes this action any more but rows it wrote earlier must keep their classification (gate
    /// §15). A retired action keeps its entry, outcome map, defects and legacy target rule exactly as they were, so old rows read
    /// as before. It declares no detail schema, has no typed producer and no routine outcomes: a retired action's rows are
    /// finite history, and a routine marking would be a present-day noise decision about an action that is no longer written.
    /// Retiring an action is a reviewed change; a catalog test pins the exact retired set.
    /// </summary>
    public bool Retired { get; init; }

    /// <summary>Why and when the action stopped being written, as one factual sentence. Required when <see cref="Retired"/> is true; not runtime-visible.</summary>
    public string? RetiredReason { get; init; }
}

/// <summary>
/// Every action name written today, each declared once (§11.7). The recorder refuses an undeclared action; the append
/// bridge classifies one as <c>other</c> and logs it. Coverage of the catalog is pinned by tests (M1, §21).
/// </summary>
public sealed class AuditActionCatalog
{
    private static readonly HashSet<string> GenericOutcomes =
        new(StringComparer.Ordinal) { AuditOutcomeCodes.Succeeded, AuditOutcomeCodes.Failed, AuditOutcomeCodes.Rejected, AuditOutcomeCodes.Denied };

    private readonly Dictionary<string, AuditActionDefinition> _definitions;

    public AuditActionCatalog(IEnumerable<AuditActionDefinition> definitions)
    {
        _definitions = new Dictionary<string, AuditActionDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            Check(definition);
            if (!_definitions.TryAdd(definition.Action, definition))
                throw new ArgumentException($"Audit action '{definition.Action}' is declared twice.", nameof(definitions));
        }
    }

    /// <summary>The production catalog.</summary>
    public static AuditActionCatalog Default { get; } = new(AuditActionCatalogEntries.All);

    public IReadOnlyCollection<AuditActionDefinition> Definitions => _definitions.Values;

    public AuditActionDefinition? Find(string action) => _definitions.GetValueOrDefault(action);

    public AuditActionDefinition Get(string action) =>
        Find(action) ?? throw new AuditInvariantException($"Audit action '{action}' is not declared in the audit catalog.");

    /// <summary>A copy of this catalog with extra definitions (test harnesses only).</summary>
    internal AuditActionCatalog With(params AuditActionDefinition[] extra) => new(_definitions.Values.Concat(extra));

    /// <summary>
    /// The normalized outcome of a raw value written by a writer of <paramref name="definition"/> (§11.5 table): the action's
    /// own mapping first, then <c>succeeded</c>, <c>failed</c>, <c>rejected</c> and <c>denied</c> as themselves. Null and
    /// anything else is <see cref="AuditOutcomeCodes.Unknown"/>.
    /// </summary>
    public static string MapRecordedOutcome(AuditActionDefinition? definition, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return AuditOutcomeCodes.Unknown;
        if (definition is not null && definition.OutcomeMap.TryGetValue(raw, out var mapped))
            return mapped;
        return GenericOutcomes.Contains(raw) ? raw : AuditOutcomeCodes.Unknown;
    }

    private static void Check(AuditActionDefinition d)
    {
        void Require(bool condition, string what)
        {
            if (!condition)
                throw new ArgumentException($"Audit action '{d.Action}': {what}.");
        }

        Require(!string.IsNullOrWhiteSpace(d.Action) && d.Action.Length <= 128, "the name is non-empty and at most 128 characters");
        Require(AuditCategories.All.Contains(d.Category) && d.Category != AuditCategories.Other, "the category is declared");
        Require(d.Category != AuditCategories.DataAccess, "data_access is reserved and has no RC1 writer");
        Require(AuditSeverities.All.Contains(d.Severity), "the severity is declared");
        Require(d.TargetType is null || AuditTargetTypes.All.Contains(d.TargetType), "the target type is declared");
        Require(AuditBases.All.Contains(d.Basis) && d.Basis != AuditBases.Unspecified, "the basis is declared");
        Require(d.RoutineOutcomes.All(AuditOutcomeCodes.All.Contains), "routine outcomes are outcome codes");
        Require(!d.RoutineOutcomes.Contains(AuditOutcomeCodes.Unknown), "an unknown outcome is never routine");
        Require(d.OutcomeMap.Values.All(AuditOutcomeCodes.All.Contains), "mapped outcomes are outcome codes");
        Require(!(d.EraInvariant && d.IntroducedAfterEpic37Migration), "era invariance applies only to actions older than Epic 37");
        Require(!string.IsNullOrWhiteSpace(d.IntroducedIn), "the introducing commit is recorded");
        Require(
            d.Defects.All(x => AuditAttributionIssues.All.Contains(x.Code) && AuditIssueCertainties.All.Contains(x.Certainty)),
            "defects use the declared codes and certainties");
        Require(d.Details is null || d.WriteClass != AuditWriteClass.ListedException, "listed exceptions have no recorder producer");
        Require(d.Retired || string.IsNullOrWhiteSpace(d.RetiredReason), "a retirement reason belongs only to a retired action");
        Require(!d.Retired || !string.IsNullOrWhiteSpace(d.RetiredReason), "a retired action states why it is retired");
        Require(!d.Retired || d.Details is null, "a retired action has no detail schema because nothing produces it");
        Require(!d.Retired || d.RoutineOutcomes.Count == 0, "a retired action has no routine outcomes");
    }
}
