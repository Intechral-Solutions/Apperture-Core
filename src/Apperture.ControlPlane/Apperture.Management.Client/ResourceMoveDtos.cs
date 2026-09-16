using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Management.Client;

/// <summary>
/// Client-side mirror of <c>Apperture.Core.Application.ResourceContexts.ResourceMovePreview</c> (CTX-3B).
/// <see cref="Source"/>/<see cref="Target"/> reuse the domain <see cref="ResourceContextRef"/> directly,
/// exactly as <see cref="IntegrationDto.ResourceContext"/> already does -- this client project references
/// Apperture.Core.Domain, not Apperture.Core.Application.
/// </summary>
public sealed record ResourceMovePreviewDto(
    string ResourceType,
    string ResourceId,
    ResourceContextRef Source,
    ResourceContextRef Target,
    bool CanMove,
    IReadOnlyList<ResourceMoveBlockerDto> Blockers,
    IReadOnlyList<ResourceMoveEffectDto> Effects);

/// <summary>Mirrors <c>ResourceMoveBlocker</c>.</summary>
public sealed record ResourceMoveBlockerDto(
    string Code,
    string Message,
    string? RelatedResourceType,
    string? RelatedResourceId,
    string? RelatedDisplayName,
    string? RelatedResourceContextId);

/// <summary>Mirrors <c>ResourceMoveEffect</c>.</summary>
public sealed record ResourceMoveEffectDto(
    string Code,
    string Message,
    string? RelatedResourceType,
    IReadOnlyList<string>? RelatedResourceIds);

/// <summary>Request body for <c>POST .../ownership/move</c>.</summary>
public sealed record ResourceOwnershipMoveRequest(string TargetResourceContextId);

/// <summary>
/// Client-side mirror of the server's blocker codes. The client cannot reference Core.Application, so
/// these are duplicated deliberately and pinned by <c>ResourceMoveDtoContractParityTests</c>. Never
/// compare a raw string literal against <see cref="ResourceMoveBlockerDto.Code"/> anywhere in the UI.
/// </summary>
public static class ResourceMoveBlockerCodes
{
    public const string TargetNotFound = "target_not_found";
    public const string TargetArchived = "target_archived";
    public const string AlreadyInTarget = "already_in_target";
    public const string BindingConnectionUnreachable = "binding_connection_unreachable";
    public const string ScheduleContextInconsistent = "schedule_context_inconsistent";
    public const string BindingIntegrationUnreachable = "binding_integration_unreachable";
    public const string UnownedBindingRequiresShared = "unowned_binding_requires_shared";
    public const string ScheduleAttachedToIntegration = "schedule_attached_to_integration";
}

/// <summary>Client-side mirror of the server's effect codes. See <see cref="ResourceMoveBlockerCodes"/>.</summary>
public static class ResourceMoveEffectCodes
{
    public const string SchedulesMoveWithIntegration = "schedules_move_with_integration";
    public const string BindingReferencesMissingConnection = "binding_references_missing_connection";
    public const string BindingReferencesMissingIntegration = "binding_references_missing_integration";
    public const string UsedByIntegration = "used_by_integration";
}
