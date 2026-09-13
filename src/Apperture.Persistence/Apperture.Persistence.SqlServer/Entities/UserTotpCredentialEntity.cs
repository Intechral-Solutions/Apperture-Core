namespace Apperture.Persistence.SqlServer;

/// <summary>Native TOTP credential for a platform user. Secret is stored only in protected form.</summary>
public sealed class UserTotpCredentialEntity
{
    public string UserId { get; set; } = "";
    public byte[] ProtectedSecret { get; set; } = [];
    public string ProtectorKeyId { get; set; } = "";
    public bool IsEnrolled { get; set; }
    public long? LastUsedStep { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? EnrolledAtUtc { get; set; }
    public DateTimeOffset? LastVerifiedAtUtc { get; set; }
    public byte[]? RowVersion { get; set; }
}

/// <summary>Hashed one-time MFA recovery code. Plaintext is never stored.</summary>
public sealed class UserMfaRecoveryCodeEntity
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public string Algorithm { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
}
