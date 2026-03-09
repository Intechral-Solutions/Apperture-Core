namespace Apperture.Persistence.SqlServer;

public sealed class SecretAuditEntity
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string EventType { get; set; } = "";
    public Guid SecretId { get; set; }
    public string? Version { get; set; }
    public string Actor { get; set; } = "";
    public string Outcome { get; set; } = "";
}
