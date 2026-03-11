namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Join table for user-to-role assignments.
/// </summary>
public sealed class UserRoleEntity
{
    public string UserId { get; set; } = "";
    public string RoleId { get; set; } = "";
}
