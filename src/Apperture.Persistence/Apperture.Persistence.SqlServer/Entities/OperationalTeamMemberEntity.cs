namespace Apperture.Persistence.SqlServer;

public sealed class OperationalTeamMemberEntity
{
    public string Id { get; set; } = "";
    public string OperationalTeamId { get; set; } = "";
    public string MemberKind { get; set; } = "";
    public string? PrincipalUserId { get; set; }
    public string? EmailAddress { get; set; }
    public string? ManagedContactId { get; set; }
    public string? DisplayName { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
