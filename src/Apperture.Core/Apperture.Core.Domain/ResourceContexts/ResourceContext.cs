namespace Apperture.Core.Domain.ResourceContexts;

/// <summary>
/// An ownership namespace for operator resources. A ResourceContext adds an ownership
/// <i>dimension</i>; it is not an identity system.
/// </summary>
/// <remarks>
/// <para>
/// Resource identifiers stay globally unique and canonical: <c>/integrations/{id}</c>,
/// <c>/connections/{id}</c> and <c>/schedules/{id}</c> resolve by id and never require a
/// ResourceContext in the route.
/// </para>
/// <para>
/// <b>ResourceContextId is ownership / namespace. ScopeKey is authorization.</b> The two concepts are
/// adjacent but unrelated; nothing in this type participates in authorization decisions.
/// </para>
/// <para>
/// ResourceContexts are archiveable, never hard-deleted through supported product operations, because
/// historical provenance resolves a recorded id back to this row for display.
/// </para>
/// </remarks>
/// <param name="Id">Globally unique identifier. The reserved Shared context uses the literal <c>"shared"</c>.</param>
/// <param name="Name">Operator-facing display name. Not unique.</param>
/// <param name="Slug">Normalized address value. Unique, case-insensitively. Never identity.</param>
/// <param name="Kind">One of <see cref="ResourceContextKinds"/>.</param>
/// <param name="State">One of <see cref="ResourceContextStates"/>.</param>
/// <param name="IsReserved">True only for the Shared context, which cannot be renamed, re-slugged, archived or deleted.</param>
/// <param name="Description">Optional operator note.</param>
/// <param name="CreatedAtUtc">Creation instant.</param>
/// <param name="UpdatedAtUtc">Last modification instant.</param>
public sealed record ResourceContext(
    ResourceContextId Id,
    string Name,
    string Slug,
    string Kind,
    string State,
    bool IsReserved,
    string? Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    /// <summary>True when this context may own newly created resources.</summary>
    public bool IsActive => string.Equals(State, ResourceContextStates.Active, StringComparison.Ordinal);

    /// <summary>Compact reference shape for detail DTOs.</summary>
    public ResourceContextRef ToRef() => new(Id.Value, Name, Slug);
}

/// <summary>
/// Compact ResourceContext reference for exposure on detail DTOs. Resolved from a cohesive query,
/// never one lookup per resource row.
/// </summary>
public sealed record ResourceContextRef(string Id, string Name, string Slug);
