namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Every username ever assigned. The primary key is the name, so each name is bound to exactly one
/// <see cref="UserId"/> forever; <see cref="UserId"/> is never updated. Null <see cref="RetiredAtUtc"/>
/// marks the user's current name (at most one per user).
/// </summary>
public sealed class UsernameLedgerEntity
{
    public string Username { get; set; } = "";
    public string UserId { get; set; } = "";

    /// <summary>First assignment. A later reclaim does not change it; reclaim history lives in audit.</summary>
    public DateTimeOffset AssignedAtUtc { get; set; }

    public DateTimeOffset? RetiredAtUtc { get; set; }

    /// <summary><c>rename</c>, <c>canonicalization</c> or <c>backfill_previous</c>; cleared on reclaim.</summary>
    public string? RetiredReason { get; set; }
}
