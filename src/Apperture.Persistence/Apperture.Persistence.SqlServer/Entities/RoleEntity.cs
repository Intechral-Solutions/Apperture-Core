namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Persisted platform role (named set of permissions).
/// </summary>
public sealed class RoleEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string PermissionsJson { get; set; } = "[]";
    public bool IsSystemRole { get; set; }
    public byte[]? RowVersion { get; set; }
}
