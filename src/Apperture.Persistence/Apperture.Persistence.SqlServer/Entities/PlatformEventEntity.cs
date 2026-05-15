namespace Apperture.Persistence.SqlServer;

/// <summary>Durable platform event (Epic 77). CloudEvents-inspired envelope.</summary>
public sealed class PlatformEventEntity
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Source { get; set; } = "";
    public string SpecVersion { get; set; } = "1.0";
    public string DataContentType { get; set; } = "application/json";
    public string DataJson { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string Severity { get; set; } = "";
    public string? Subject { get; set; }
    public string? DedupeKey { get; set; }
    public string? IntegrationId { get; set; }
    public string? ModuleId { get; set; }
    public string? ProcedureId { get; set; }
    public string? RunId { get; set; }
    public string? ScheduleId { get; set; }
    public string? ActorId { get; set; }
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
}
