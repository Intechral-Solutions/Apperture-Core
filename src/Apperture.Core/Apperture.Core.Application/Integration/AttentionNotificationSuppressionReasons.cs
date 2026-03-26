namespace Apperture.Core.Application.Integration;

public static class AttentionNotificationSuppressionReasons
{
    public const string PolicyDisabled = "policy_disabled";
    public const string NoRecipientsConfigured = "no_recipients_configured";
    public const string SmtpNotConfigured = "smtp_not_configured";
    public const string SmtpFromNotConfigured = "smtp_from_not_configured";
    public const string DuplicateActivationSuppressed = "duplicate_activation_suppressed";
    public const string ReminderNotDue = "reminder_not_due";
    public const string RemindersSuppressedAcknowledged = "reminders_suppressed_acknowledged";
    public const string ResolvedNotificationDisabled = "resolved_notification_disabled";
    public const string NoPriorActivationForResolved = "no_prior_activation_for_resolved";
    public const string NoRouteConfigured = "no_route_configured";
    public const string EscalationSuppressedAcknowledged = "escalation_suppressed_acknowledged";
    public const string EscalationSuppressedNoThreshold = "escalation_suppressed_no_threshold";
    public const string EscalationAlreadySent = "escalation_already_sent";
    public const string EscalationNotDue = "escalation_not_due";
}

