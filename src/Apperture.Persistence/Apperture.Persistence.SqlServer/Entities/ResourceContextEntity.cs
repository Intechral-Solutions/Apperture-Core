namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Persisted ResourceContext: the ownership namespace dimension for operator resources.
/// </summary>
/// <remarks>
/// Rows are never removed by supported product operations. Retirement sets <see cref="State"/> to
/// Archived so historical provenance can still resolve an id to a display name.
/// </remarks>
public sealed class ResourceContextEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Normalized, unique address value. Never identity.</summary>
    public string Slug { get; set; } = "";

    /// <summary>String, not an enum column, so new kinds are additive without a migration.</summary>
    public string Kind { get; set; } = "";

    public string State { get; set; } = "";

    /// <summary>True only for the reserved Shared context.</summary>
    public bool IsReserved { get; set; }

    public string? Description { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[]? RowVersion { get; set; }
}
