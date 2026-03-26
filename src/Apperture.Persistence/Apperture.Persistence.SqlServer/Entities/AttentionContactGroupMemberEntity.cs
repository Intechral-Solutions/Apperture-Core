namespace Apperture.Persistence.SqlServer;

public sealed class AttentionContactGroupMemberEntity
{
    public string Id { get; set; } = "";
    public string AttentionContactGroupId { get; set; } = "";
    public string EmailAddress { get; set; } = "";
    public string? DisplayName { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
