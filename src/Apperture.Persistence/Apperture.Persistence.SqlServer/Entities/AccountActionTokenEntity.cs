namespace Apperture.Persistence.SqlServer;

/// <summary>Hashed one-time Apperture account-action token. Bearer material is never stored.</summary>
public sealed class AccountActionTokenEntity
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public int Purpose { get; set; }
    public string TokenHash { get; set; } = "";
    public string? DestinationEmail { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
