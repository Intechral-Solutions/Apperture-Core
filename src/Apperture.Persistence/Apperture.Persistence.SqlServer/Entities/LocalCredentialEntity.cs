namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Persisted local (username/password) credential for a platform user. One row per user for local auth.
/// </summary>
public sealed class LocalCredentialEntity
{
    public string UserId { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public string Algorithm { get; set; } = "";
    public bool IsDisabled { get; set; }
    public byte[]? RowVersion { get; set; }
}
