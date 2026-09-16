using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Core.Application.ResourceContexts;

/// <summary>
/// Advisory result of validating (preview) or attempting (move) a resource ownership change.
/// Preview and move share this exact shape (design gate §8.2) so a UI can render either
/// identically, and so move's 409 response body is literally this type.
/// </summary>
public sealed record ResourceMovePreview(
    string ResourceType,
    string ResourceId,
    ResourceContextRef Source,
    ResourceContextRef Target,
    bool CanMove,
    IReadOnlyList<ResourceMoveBlocker> Blockers,
    IReadOnlyList<ResourceMoveEffect> Effects);

/// <summary>
/// A reason a move cannot proceed. <see cref="RelatedResourceType"/>/<see cref="RelatedResourceId"/>/
/// <see cref="RelatedDisplayName"/>/<see cref="RelatedResourceContextId"/> are populated when the
/// blocker concerns a specific other resource (e.g. an unreachable Connection), and are all null for
/// the common, resource-less blockers (§8.3).
/// </summary>
public sealed record ResourceMoveBlocker(
    string Code,
    string Message,
    string? RelatedResourceType,
    string? RelatedResourceId,
    string? RelatedDisplayName,
    string? RelatedResourceContextId);

/// <summary>
/// A non-blocking consequence of a move the operator should see before submitting (e.g. "3 schedules
/// move with this integration"), or an advisory note the move does not make worse (§8.4 step 2).
/// </summary>
public sealed record ResourceMoveEffect(
    string Code,
    string Message,
    string? RelatedResourceType = null,
    IReadOnlyList<string>? RelatedResourceIds = null);

/// <summary>Stable resource-type discriminators for <see cref="ResourceMovePreview.ResourceType"/> and blocker/effect related-resource fields.</summary>
public static class ResourceMoveResourceTypes
{
    public const string Integration = "Integration";
    public const string Connection = "Connection";
    public const string Schedule = "Schedule";
    public const string ResourceContext = "ResourceContext";
    public const string Binding = "Binding";
}

/// <summary>Stable blocker codes (API and tests). See design gate §8.3–§8.6.</summary>
public static class ResourceMoveBlockerCodes
{
    /// <summary>Common (§8.3): the target ResourceContext id does not resolve to any row.</summary>
    public const string TargetNotFound = "target_not_found";

    /// <summary>Common (§8.3): the target ResourceContext exists but is not Active.</summary>
    public const string TargetArchived = "target_archived";

    /// <summary>Common (§8.3): source and target are the same ResourceContext.</summary>
    public const string AlreadyInTarget = "already_in_target";

    /// <summary>Integration move (§8.4): a bound Connection would become unreachable from the target.</summary>
    public const string BindingConnectionUnreachable = "binding_connection_unreachable";

    /// <summary>Integration move (§8.4): an attached Schedule's current context does not match the Integration's source context.</summary>
    public const string ScheduleContextInconsistent = "schedule_context_inconsistent";

    /// <summary>Connection move (§8.5): a bound Integration (including Retired/Inactive) would no longer be able to reference this Connection.</summary>
    public const string BindingIntegrationUnreachable = "binding_integration_unreachable";

    /// <summary>Connection move (§8.5): a legacy/global binding with no owning Integration requires the Connection to stay Shared.</summary>
    public const string UnownedBindingRequiresShared = "unowned_binding_requires_shared";

    /// <summary>Schedule move (§8.6): an Integration-attached Schedule cannot move independently.</summary>
    public const string ScheduleAttachedToIntegration = "schedule_attached_to_integration";
}

/// <summary>Stable effect codes (API and tests). See design gate §8.4–§8.5.</summary>
public static class ResourceMoveEffectCodes
{
    /// <summary>Integration move (§8.4 step 4): schedules that move atomically with the Integration.</summary>
    public const string SchedulesMoveWithIntegration = "schedules_move_with_integration";

    /// <summary>Integration move (§8.4 step 2): a binding references a Connection row that no longer exists; advisory, never a blocker.</summary>
    public const string BindingReferencesMissingConnection = "binding_references_missing_connection";

    /// <summary>Connection move (§8.5): "Used by" blast radius, shown even when CanMove is true.</summary>
    public const string UsedByIntegration = "used_by_integration";

    /// <summary>Connection move (§8.5): a binding references an Integration row that no longer exists; advisory, never a blocker. Mirror of <see cref="BindingReferencesMissingConnection"/>.</summary>
    public const string BindingReferencesMissingIntegration = "binding_references_missing_integration";
}
