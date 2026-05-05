namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Canonical person/contact record for ownership and notifications (may exist without login).
/// Epic 76.
/// </summary>
public sealed class ManagedContactEntity
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? EmailAddress { get; set; }
    /// <summary>Custom or Entra (application constants ManagedContactSources).</summary>
    public string Source { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public string? LinkedUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
