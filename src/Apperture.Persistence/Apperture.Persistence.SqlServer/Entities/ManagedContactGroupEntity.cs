namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Imported Entra group metadata for admin organization; membership sync is out of scope for Epic 76.
/// </summary>
public sealed class ManagedContactGroupEntity
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>Source marker (e.g. Entra).</summary>
    public string Source { get; set; } = "";
    public string EntraGroupObjectId { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
