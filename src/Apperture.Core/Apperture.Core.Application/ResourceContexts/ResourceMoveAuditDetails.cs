using System.Text.Json.Serialization;

namespace Apperture.Core.Application.ResourceContexts;

/// <summary>JSON payload for a successful move's moved_out/moved_in audit pair (design gate §8.8). Both records carry the identical payload.</summary>
public sealed record ResourceMoveAuditDetails(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("resourceId")] string ResourceId,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("sourceResourceContextId")] string SourceResourceContextId,
    [property: JsonPropertyName("targetResourceContextId")] string TargetResourceContextId,
    [property: JsonPropertyName("movedScheduleIds")] IReadOnlyList<string>? MovedScheduleIds = null);

/// <summary>JSON payload for a blocked move's single move_rejected audit record.</summary>
public sealed record ResourceMoveRejectedAuditDetails(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("resourceId")] string ResourceId,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("sourceResourceContextId")] string SourceResourceContextId,
    [property: JsonPropertyName("targetResourceContextId")] string TargetResourceContextId,
    [property: JsonPropertyName("blockerCodes")] IReadOnlyList<string> BlockerCodes);
