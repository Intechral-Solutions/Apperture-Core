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

    /// <summary>The identity of the stored secret: new whenever the secret is (re)written, unchanged by enrolling or signing in.</summary>
    public string CredentialId { get; set; } = "";

    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// A candidate secret staged to replace an enrolled authenticator. It lives beside the active credential and never in its row:
/// until the switch commits, nothing about the active credential differs from what it was.
/// </summary>
public sealed class UserTotpReplacementCandidateEntity
{
    public string CandidateId { get; set; } = "";
    public string UserId { get; set; } = "";

    /// <summary>Protected as the active secret is. Emptied when the candidate is superseded or consumed.</summary>
    public byte[] ProtectedSecret { get; set; } = [];

    public string ProtectorKeyId { get; set; } = "";

    /// <summary>The <see cref="UserTotpCredentialEntity.CredentialId"/> that was active when the candidate was staged.</summary>
    public string StagedAgainstCredentialId { get; set; } = "";

    /// <summary>0 staged, 1 verified, 2 superseded, 3 consumed. A live candidate (0 or 1) is unique per user.</summary>
    public int Status { get; set; }

    /// <summary>Starts at 1 and increases by one with every state change, so a stale holder of an earlier state is detected.</summary>
    public int Version { get; set; }

    /// <summary>Set by the database's clock, as is every expiry decision.</summary>
    public DateTime CreatedUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? VerifiedUtc { get; set; }
    public long? MatchedStep { get; set; }
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
