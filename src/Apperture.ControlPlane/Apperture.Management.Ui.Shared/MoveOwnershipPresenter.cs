using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// One line of operator copy in the move dialog. <see cref="Text"/> is plain prose; when
/// <see cref="LinkText"/> and <see cref="Href"/> are set they render as a link after (or, when
/// <see cref="Text"/> is empty, instead of) the prose. Ids appear only in <see cref="Href"/>.
/// </summary>
public sealed record MoveItem(string Text, string? LinkText = null, string? Href = null);

/// <summary>The dialog body for a preview, grouped into the sections the dialog renders.</summary>
/// <param name="WillMove">What moves with the resource. Empty when the move is blocked.</param>
/// <param name="UsedBy">Resources that depend on the moving resource. Shown whether allowed or blocked.</param>
/// <param name="BlockedBy">Actionable reasons the move is blocked: a sentence plus an optional link.</param>
/// <param name="Notes">Informational effects (e.g. dangling bindings) and any unrecognised effect.</param>
/// <param name="History">The one-line statement about where historical activity stays.</param>
public sealed record MovePresentation(
    IReadOnlyList<MoveItem> WillMove,
    IReadOnlyList<MoveItem> UsedBy,
    IReadOnlyList<MoveItem> BlockedBy,
    IReadOnlyList<MoveItem> Notes,
    string History);

/// <summary>A related resource whose display name the dialog should look up before calling <see cref="MoveOwnershipPresenter.Present"/>.</summary>
public readonly record struct MoveRelatedRef(string ResourceType, string Id);

/// <summary>
/// HSK-B: pure mapping from a move preview -- blocker/effect codes plus their structured fields -- to
/// operator copy. Performs no I/O; the razor component resolves names (context names via the
/// <c>describeContext</c> delegate, related resource names via <see cref="GetRelatedNamesToResolve"/> and
/// the <c>relatedNames</c> dictionary) and hands them in.
/// </summary>
/// <remarks>
/// <para>
/// Copy for every known code uses display names and never a raw context id or raw resource id in
/// <see cref="MoveItem.Text"/>/<see cref="MoveItem.LinkText"/>. Three server messages
/// (<c>target_not_found</c>, <c>binding_connection_unreachable</c>, <c>binding_integration_unreachable</c>)
/// embed raw ids, so they are deliberately not reused. <c>preview.Target.Name</c> is never shown for
/// <c>target_not_found</c> (the server sets it to the raw id when the context is missing).
/// </para>
/// <para>
/// Unknown blocker/effect codes fall back to the server <c>Message</c> so they stay readable. Codes are
/// always compared through <see cref="ResourceMoveBlockerCodes"/>/<see cref="ResourceMoveEffectCodes"/>.
/// </para>
/// </remarks>
public static class MoveOwnershipPresenter
{
    /// <summary>Most related names listed (and therefore resolved) per resource type; the rest are summarised as a count.</summary>
    public const int MaxResolvedNames = 10;

    /// <summary>Resource-type token for integrations, as used by <see cref="MoveRelatedRef"/> and <see cref="RelatedNameKey"/>.</summary>
    public const string IntegrationResourceType = "Integration";

    /// <summary>Resource-type token for schedules, as used by <see cref="MoveRelatedRef"/> and <see cref="RelatedNameKey"/>.</summary>
    public const string ScheduleResourceType = "Schedule";

    private const string UnknownContextFallback = "Unknown context";

    /// <summary>Key of a resolved related name in the dictionary passed to <see cref="Present"/>.</summary>
    public static string RelatedNameKey(string resourceType, string id) => $"{resourceType}:{id}";

    /// <summary>
    /// The related resources whose names would improve the copy: integrations from <c>used_by_integration</c>
    /// and schedules from <c>schedules_move_with_integration</c>. De-duplicated, at most
    /// <see cref="MaxResolvedNames"/> per type, empty when there are none.
    /// </summary>
    public static IReadOnlyList<MoveRelatedRef> GetRelatedNamesToResolve(ResourceMovePreviewDto preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var effects = preview.Effects ?? [];
        var refs = new List<MoveRelatedRef>();

        refs.AddRange(CollectIds(effects, ResourceMoveEffectCodes.UsedByIntegration)
            .Take(MaxResolvedNames)
            .Select(id => new MoveRelatedRef(IntegrationResourceType, id)));
        refs.AddRange(CollectIds(effects, ResourceMoveEffectCodes.SchedulesMoveWithIntegration)
            .Take(MaxResolvedNames)
            .Select(id => new MoveRelatedRef(ScheduleResourceType, id)));

        return refs;
    }

    /// <summary>
    /// The context ids whose display names <see cref="Present"/> will ask <c>describeContext</c> for: the
    /// preview's source and target, plus every blocker's owning context. Distinct, non-blank, in that
    /// order; null <c>Source</c>/<c>Target</c>/<c>Blockers</c> and null entries are tolerated.
    /// </summary>
    /// <remarks>
    /// Exists so the razor can pre-warm <c>ResourceContextViewState</c> (otherwise an owner context that is
    /// not yet in the known list renders as "Unknown context") without naming any blocker DTO field itself
    /// -- the dialog renders presenter output only, which <c>MovementUiSourceTests</c> pins.
    /// </remarks>
    public static IReadOnlyList<string> GetContextIdsToResolve(ResourceMovePreviewDto preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var ids = new List<string?> { preview.Source?.Id, preview.Target?.Id };
        ids.AddRange((preview.Blockers ?? []).Where(b => b is not null).Select(b => b.RelatedResourceContextId));

        return ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Maps a preview to the dialog's sections. See the type remarks for the copy rules.</summary>
    /// <param name="preview">The server preview. <c>Source</c>/<c>Target</c> and the lists may be null.</param>
    /// <param name="sourceName">Display name of the current owner context.</param>
    /// <param name="targetName">Display name of the selected target context (never the raw-id <c>Target.Name</c>).</param>
    /// <param name="resourceName">Display name of the resource being moved, when the caller knows it.</param>
    /// <param name="describeContext">Context id to display name (e.g. <c>ResourceContextViewState.DescribeContext</c>).</param>
    /// <param name="relatedNames">Resolved related names keyed by <see cref="RelatedNameKey"/>; unresolved ids fall back to counts.</param>
    public static MovePresentation Present(
        ResourceMovePreviewDto preview,
        string sourceName,
        string targetName,
        string? resourceName,
        Func<string, string> describeContext,
        IReadOnlyDictionary<string, string>? relatedNames = null)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(describeContext);

        var effects = (preview.Effects ?? []).Where(e => e is not null).ToList();
        var blockers = (preview.Blockers ?? []).Where(b => b is not null).ToList();
        var noun = NounFor(preview.ResourceType);

        return new MovePresentation(
            WillMove: preview.CanMove ? BuildWillMove(noun, resourceName, effects, relatedNames) : [],
            UsedBy: BuildUsedBy(effects, relatedNames),
            BlockedBy: blockers.Select(b => BuildBlocker(b, preview, noun, targetName, describeContext)).ToList(),
            Notes: BuildNotes(effects),
            History: $"Historical activity remains attributed to {sourceName}.");
    }

    // ---- sections ----------------------------------------------------------------------------------

    private static List<MoveItem> BuildWillMove(
        string noun,
        string? resourceName,
        IReadOnlyList<ResourceMoveEffectDto> effects,
        IReadOnlyDictionary<string, string>? relatedNames)
    {
        var items = new List<MoveItem>
        {
            string.IsNullOrWhiteSpace(resourceName)
                ? new MoveItem($"This {noun}")
                : new MoveItem($"{Capitalize(noun)} '{resourceName}'")
        };

        var scheduleIds = CollectIds(effects, ResourceMoveEffectCodes.SchedulesMoveWithIntegration);
        if (scheduleIds.Count == 0)
            return items;

        var links = ResolveLinks(scheduleIds, ScheduleResourceType, relatedNames);
        if (links.Count == 0)
        {
            items.Add(new MoveItem($"{scheduleIds.Count} attached {Plural(scheduleIds.Count, "schedule")}"));
            return items;
        }

        items.AddRange(links.Select(l => new MoveItem("Schedule", l.Name, $"/schedules/{l.Id}")));

        var remainder = scheduleIds.Count - links.Count;
        if (remainder > 0)
            items.Add(new MoveItem($"{remainder} more attached {Plural(remainder, "schedule")}"));

        return items;
    }

    private static List<MoveItem> BuildUsedBy(
        IReadOnlyList<ResourceMoveEffectDto> effects,
        IReadOnlyDictionary<string, string>? relatedNames)
    {
        var integrationIds = CollectIds(effects, ResourceMoveEffectCodes.UsedByIntegration);
        if (integrationIds.Count == 0)
            return [];

        var links = ResolveLinks(integrationIds, IntegrationResourceType, relatedNames);
        if (links.Count == 0)
            return [new MoveItem($"Used by {integrationIds.Count} {Plural(integrationIds.Count, "integration")}")];

        var items = links.Select(l => new MoveItem("", l.Name, $"/integrations/{l.Id}")).ToList();

        var remainder = integrationIds.Count - links.Count;
        if (remainder > 0)
            items.Add(new MoveItem($"and {remainder} more"));

        return items;
    }

    private static List<MoveItem> BuildNotes(IReadOnlyList<ResourceMoveEffectDto> effects)
    {
        var notes = new List<MoveItem>();
        foreach (var effect in effects)
        {
            switch (effect.Code)
            {
                case ResourceMoveEffectCodes.SchedulesMoveWithIntegration:
                case ResourceMoveEffectCodes.UsedByIntegration:
                    // Rendered in WillMove / UsedBy.
                    break;

                case ResourceMoveEffectCodes.BindingReferencesMissingConnection:
                case ResourceMoveEffectCodes.BindingReferencesMissingIntegration:
                    // The server message names only a slot ("Binding for slot 'x' ..."), so it is safe verbatim.
                    AddMessage(notes, effect.Message);
                    break;

                default:
                    // Unknown effect: the server message keeps it readable.
                    AddMessage(notes, effect.Message);
                    break;
            }
        }

        return notes;
    }

    private static MoveItem BuildBlocker(
        ResourceMoveBlockerDto blocker,
        ResourceMovePreviewDto preview,
        string noun,
        string targetName,
        Func<string, string> describeContext)
    {
        var target = TargetLabel(targetName);
        var relatedName = NonBlank(blocker.RelatedDisplayName);

        switch (blocker.Code)
        {
            case ResourceMoveBlockerCodes.TargetNotFound:
                // Never Target.Name / the server message: both carry the raw target id.
                return new MoveItem("The selected target context no longer exists.");

            case ResourceMoveBlockerCodes.TargetArchived:
                return new MoveItem(
                    $"{relatedName ?? NonBlank(targetName) ?? "The selected context"} is archived and can't receive new ownership.");

            case ResourceMoveBlockerCodes.AlreadyInTarget:
                return new MoveItem($"This {noun} is already in {target}.");

            case ResourceMoveBlockerCodes.BindingConnectionUnreachable:
            {
                // Remedy verified against ResourceOwnershipMoveService's integration validator: the blocker
                // fires when CanReference(targetId, connection.ResourceContextId) is false, i.e. the
                // connection's owner is neither the target nor Shared. Moving the connection into the target
                // or into Shared each make it reachable, so both remedies are exactly what the rule implies.
                var subject = relatedName is null ? "A connection" : $"Connection '{relatedName}'";
                var owner = Describe(describeContext, NonBlank(blocker.RelatedResourceContextId) ?? ResourceContextIds.SharedValue);
                var remedy = ResourceContextIds.IsShared(preview.Target?.Id)
                    ? "Move the connection to Shared first, or choose another destination."
                    : $"Move the connection to {target} or Shared first, or choose another destination.";
                var (linkText, href) = Link(blocker.RelatedResourceId, "View connection", "/connections/");
                return new MoveItem(
                    $"{subject} is owned by {owner}, so it would become unreachable from {target}. {remedy}",
                    linkText, href);
            }

            case ResourceMoveBlockerCodes.ScheduleContextInconsistent:
            {
                // The server message carries the schedule name only (no ids), so it is safe to reuse.
                var (linkText, href) = Link(blocker.RelatedResourceId, "View schedule", "/schedules/");
                return new MoveItem(MessageOrDefault(blocker.Message), linkText, href);
            }

            case ResourceMoveBlockerCodes.BindingIntegrationUnreachable:
            {
                var subject = relatedName is null ? "An integration" : $"Integration '{relatedName}'";
                var contextId = NonBlank(blocker.RelatedResourceContextId);
                var location = contextId is null ? "" : $" in {Describe(describeContext, contextId)}";
                var (linkText, href) = Link(blocker.RelatedResourceId, "View integration", "/integrations/");
                return new MoveItem(
                    $"{subject}{location} would no longer be able to use this connection.", linkText, href);
            }

            case ResourceMoveBlockerCodes.UnownedBindingRequiresShared:
                // A binding has no page to link to; the server message is id-free.
                return new MoveItem(MessageOrDefault(blocker.Message));

            case ResourceMoveBlockerCodes.ScheduleAttachedToIntegration:
            {
                var belongsTo = relatedName is null ? "an integration" : $"integration '{relatedName}'";
                var (linkText, href) = Link(blocker.RelatedResourceId, "Go to integration", "/integrations/");
                return new MoveItem(
                    $"This schedule belongs to {belongsTo}. Move the integration instead — its schedules move with it.",
                    linkText, href);
            }

            default:
                // Unknown blocker: the server message keeps it readable; no link.
                return new MoveItem(MessageOrDefault(blocker.Message));
        }
    }

    // ---- helpers -----------------------------------------------------------------------------------

    /// <summary>Distinct, non-blank related ids across every effect carrying <paramref name="effectCode"/>, in order.</summary>
    private static List<string> CollectIds(IEnumerable<ResourceMoveEffectDto> effects, string effectCode) =>
        effects
            .Where(e => e is not null && string.Equals(e.Code, effectCode, StringComparison.Ordinal))
            .SelectMany(e => e.RelatedResourceIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Ids that have a resolved, non-blank name, in order, capped at <see cref="MaxResolvedNames"/>.</summary>
    private static List<(string Id, string Name)> ResolveLinks(
        IReadOnlyList<string> ids,
        string resourceType,
        IReadOnlyDictionary<string, string>? relatedNames)
    {
        var resolved = new List<(string Id, string Name)>();
        if (relatedNames is null)
            return resolved;

        foreach (var id in ids)
        {
            if (resolved.Count >= MaxResolvedNames)
                break;

            if (relatedNames.TryGetValue(RelatedNameKey(resourceType, id), out var name)
                && !string.IsNullOrWhiteSpace(name))
            {
                resolved.Add((id, name));
            }
        }

        return resolved;
    }

    private static (string? LinkText, string? Href) Link(string? resourceId, string linkText, string hrefPrefix) =>
        string.IsNullOrWhiteSpace(resourceId) ? (null, null) : (linkText, hrefPrefix + resourceId);

    private static string Describe(Func<string, string> describeContext, string contextId) =>
        NonBlank(describeContext(contextId)) ?? UnknownContextFallback;

    private static string TargetLabel(string? targetName) => NonBlank(targetName) ?? "the selected context";

    private static string MessageOrDefault(string? message) => NonBlank(message) ?? "This move is blocked.";

    private static void AddMessage(List<MoveItem> items, string? message)
    {
        if (NonBlank(message) is { } text)
            items.Add(new MoveItem(text));
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string NounFor(string? resourceType) =>
        resourceType?.ToLowerInvariant() switch
        {
            "integration" => "integration",
            "connection" => "connection",
            "schedule" => "schedule",
            _ => "resource"
        };

    private static string Capitalize(string noun) => char.ToUpperInvariant(noun[0]) + noun[1..];

    private static string Plural(int count, string singular) => count == 1 ? singular : singular + "s";
}
