namespace Apperture.Persistence.SqlServer;

public sealed class EntraAccessRequestEntity
{
    public string Id { get; set; } = "";
    public string RegistrationId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ProviderObjectId { get; set; } = "";
    public string? DisplayNameSnapshot { get; set; }
    public string? UserPrincipalNameSnapshot { get; set; }
    public string? MailSnapshot { get; set; }
    public string? UserTypeSnapshot { get; set; }
    public DateTimeOffset AuthenticatedAtUtc { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public int Status { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? ProvisionedUserId { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
