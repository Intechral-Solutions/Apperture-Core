namespace Apperture.Persistence.SqlServer;

public sealed class IntegrationAttentionRoutingEntity
{
    public string Id { get; set; } = "";
    public string IntegrationId { get; set; } = "";
    public string ConditionKey { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public bool SendResolvedNotification { get; set; } = true;
    public bool SuppressRemindersWhenAcknowledged { get; set; } = true;
    public bool SuppressEscalationWhenAcknowledged { get; set; } = true;
    public long? EscalationAfterTicks { get; set; }
    public long? ReminderIntervalTicks { get; set; }
    public string? PrimaryOperationalTeamId { get; set; }
    public string? EscalationOperationalTeamId { get; set; }
}
