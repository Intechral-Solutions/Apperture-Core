namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Typed personal preferences for one operator. Absence of a row means platform defaults apply, so
/// nothing needs backfilling when the table is introduced.
/// </summary>
public sealed class UserPreferenceEntity
{
    public string UserId { get; set; } = "";

    /// <summary><c>System</c>, <c>Light</c>, or <c>Dark</c>.</summary>
    public string Appearance { get; set; } = "";

    /// <summary><c>System</c>, <c>Hour12</c>, or <c>Hour24</c>.</summary>
    public string TimeFormat { get; set; } = "";

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public byte[]? RowVersion { get; set; }
}
