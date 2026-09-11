namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Persisted Integration entity. Epic 49, Epic 50 (Settings).
/// </summary>
public sealed class IntegrationEntity
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ModuleId { get; set; } = "";
    /// <summary>Authorization scope. Unrelated to <see cref="ResourceContextId"/>, which is ownership.</summary>
    public string? ScopeKey { get; set; }

    /// <summary>Owning ResourceContext. Required; existing rows were backfilled to the reserved Shared context.</summary>
    public string ResourceContextId { get; set; } = "";
    public int State { get; set; }
    public string? SettingsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[]? RowVersion { get; set; }
}
