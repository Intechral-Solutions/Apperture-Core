namespace Apperture.Persistence.SqlServer;

public sealed class TelemetryEventEntity
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string? CorrelationId { get; set; }
    public string EventType { get; set; } = "";
    public string? Payload { get; set; }
}
