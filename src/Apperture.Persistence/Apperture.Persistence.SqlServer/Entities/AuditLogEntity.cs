namespace Apperture.Persistence.SqlServer;

public sealed class AuditLogEntity
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Details { get; set; }
}
