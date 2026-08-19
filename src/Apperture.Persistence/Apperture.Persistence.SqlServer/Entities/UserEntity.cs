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

    /// <summary>Optional operator display timezone id (IANA preferred). Display only; execution stays UTC.</summary>
    public string? DisplayTimeZoneId { get; set; }

    /// <summary>Avatar discriminator: <c>builtIn</c> or <c>custom</c>. Null means the platform default.</summary>
    public string? AvatarKind { get; set; }

    /// <summary>Curated built-in avatar option id when <see cref="AvatarKind"/> is <c>builtIn</c>.</summary>
    public string? AvatarOptionId { get; set; }

    /// <summary>
    /// Platform-generated logical key for the stored custom avatar renditions. Never a filesystem
    /// path and never client-supplied; changing the image always produces a new key.
    /// </summary>
    public string? AvatarImageKey { get; set; }

    public byte[]? RowVersion { get; set; }
}
