namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Persisted Integration entity. Epic 49, Epic 50 (Settings).
/// </summary>
public sealed class IntegrationEntity
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public string? ScopeKey { get; set; }
    public int State { get; set; }
    public string? SettingsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[]? RowVersion { get; set; }
}
