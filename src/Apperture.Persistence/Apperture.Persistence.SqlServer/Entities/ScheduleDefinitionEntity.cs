namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Persisted schedule definition. Epic 28.
/// </summary>
public sealed class ScheduleDefinitionEntity
{
    public string Id { get; set; } = "";
    public string? IntegrationId { get; set; }
    public string Name { get; set; } = "";
    public string RecurrenceExpression { get; set; } = "";
    public string TimeZone { get; set; } = "";
    public bool Enabled { get; set; }
    public int TargetJobType { get; set; }
    public string? TargetJobConfigurationJson { get; set; }
    public string? ExecutionPolicyRef { get; set; }
    public int Version { get; set; }
}
