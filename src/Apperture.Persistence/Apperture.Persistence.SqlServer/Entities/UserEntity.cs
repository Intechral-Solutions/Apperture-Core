namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Persisted platform user (human operator identity).
/// </summary>
public sealed class UserEntity
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? EmailAddress { get; set; }
    public bool IsBreakGlassEligible { get; set; }
    public byte[]? RowVersion { get; set; }
}
