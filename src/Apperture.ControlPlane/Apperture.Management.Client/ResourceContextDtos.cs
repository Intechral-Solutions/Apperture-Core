namespace Apperture.Management.Client;

/// <summary>
/// CTX-3A Plan 3 (Task 1): client-side mirror of
/// <c>Apperture.Management.Api.Controllers.ResourceContextDto</c>. Field names/types are kept in
/// lockstep with the server DTO by hand (Plan 1's client/server parity convention) -- there is no
/// automated parity test covering this specific pair yet.
/// </summary>
public sealed record ResourceContextDto(
    string Id,
    string Name,
    string Slug,
    string Kind,
    string State,
    bool IsReserved,
    string? Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Mirrors <c>Apperture.Management.Api.Controllers.CreateResourceContextRequest</c>.</summary>
public sealed record CreateResourceContextRequest(string Name, string? Slug, string? Description);

/// <summary>Mirrors <c>Apperture.Management.Api.Controllers.UpdateResourceContextRequest</c>.</summary>
public sealed record UpdateResourceContextRequest(string? Name, string? Slug, string? Description);

/// <summary>
/// Client-side mirror of <c>Apperture.Management.Api.Controllers.ResourceContextOwnershipSummaryDto</c>.
/// Every field is caller-permission-masked server-side: null means "hidden from this caller," not
/// "zero." See <see cref="IManagementClient.GetResourceContextSummaryAsync"/> for the additional
/// omit-not-zero semantics that apply at the dictionary level.
/// </summary>
public sealed record ResourceContextOwnershipSummaryDto(
    int? LiveIntegrations, int? RetiredIntegrations,
    int? ActiveConnections, int? RetiredConnections,
    int? EnabledSchedules, int? DisabledSchedules);

/// <summary>Why <see cref="IManagementClient.ArchiveResourceContextAsync"/> received a 409.</summary>
public enum ResourceContextArchiveConflictReason
{
    /// <summary>
    /// The target is the reserved Shared context. Server body is a plain <c>{ error: string }</c>
    /// object; <see cref="ResourceContextArchiveConflictException.OwnershipSummary"/> is null.
    /// </summary>
    Reserved,

    /// <summary>
    /// The target still owns a live resource (integration/connection/schedule). Server body is the
    /// full <see cref="ResourceContextOwnershipSummaryDto"/>, available via
    /// <see cref="ResourceContextArchiveConflictException.OwnershipSummary"/>.
    /// </summary>
    OwnershipBlocked,

    /// <summary>
    /// Defensive fallback: the 409 body matched neither known shape. Should not happen against the
    /// current server; callers should treat this like <see cref="Reserved"/> (message-only) but not
    /// assume it is stable API contract.
    /// </summary>
    Unrecognized
}

/// <summary>
/// Thrown by <see cref="IManagementClient.ArchiveResourceContextAsync"/> when the server responds
/// 409 Conflict. <c>ResourceContextsController.Archive</c>'s XML doc is explicit that this endpoint
/// returns two semantically different 409 bodies for two different causes: a reserved-context
/// rejection is a plain <c>{ error: string }</c> object, while an ownership-blocked rejection is the
/// full <see cref="ResourceContextOwnershipSummaryDto"/>. Callers must branch on <see cref="Reason"/>
/// rather than assuming either shape is always present -- <see cref="OwnershipSummary"/> is populated
/// only when <see cref="Reason"/> is <see cref="ResourceContextArchiveConflictReason.OwnershipBlocked"/>.
/// </summary>
public sealed class ResourceContextArchiveConflictException : Exception
{
    public ResourceContextArchiveConflictReason Reason { get; }

    /// <summary>Non-null only when <see cref="Reason"/> is <see cref="ResourceContextArchiveConflictReason.OwnershipBlocked"/>.</summary>
    public ResourceContextOwnershipSummaryDto? OwnershipSummary { get; }

    public ResourceContextArchiveConflictException(
        ResourceContextArchiveConflictReason reason,
        string message,
        ResourceContextOwnershipSummaryDto? ownershipSummary)
        : base(message)
    {
        Reason = reason;
        OwnershipSummary = ownershipSummary;
    }
}
