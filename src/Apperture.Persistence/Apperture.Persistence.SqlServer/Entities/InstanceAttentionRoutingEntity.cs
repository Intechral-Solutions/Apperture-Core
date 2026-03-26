namespace Apperture.Persistence.SqlServer;

public sealed class InstanceAttentionRoutingEntity
{
    public string Id { get; set; } = "";
    public string ConditionKey { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public bool SendResolvedNotification { get; set; } = true;
    public bool SuppressRemindersWhenAcknowledged { get; set; } = true;
    public bool SuppressEscalationWhenAcknowledged { get; set; } = true;
    public long? EscalationAfterTicks { get; set; }
    public long? ReminderIntervalTicks { get; set; }
    public string? PrimaryContactGroupId { get; set; }
    public string? EscalationContactGroupId { get; set; }
}
