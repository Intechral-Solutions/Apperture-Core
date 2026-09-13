namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Operator-facing labels for notification instance-level suppression reasons.
/// </summary>
public static class NotificationInstanceSuppressionReasonLabels
{
    public static string Label(string? reason) => reason switch
    {
        "policy_disabled" => "Notification policy disabled for this integration",
        "no_route_configured" => "No deliverable recipients configured",
        "no_recipients_configured" => "No recipients configured",
        "duplicate_activation_suppressed" => "Duplicate activation suppressed",
        "resolved_notification_disabled" => "Resolved notifications disabled for routing",
        "no_prior_activation_for_resolved" => "No prior activation to resolve",
        "smtp_not_configured" => "SMTP not configured",
        "smtp_from_not_configured" => "SMTP from address not configured",
        _ => string.IsNullOrWhiteSpace(reason)
            ? "—"
            : reason.Replace("_", " ", StringComparison.Ordinal),
    };

    public static string? ActionGuidance(string? reason) => reason switch
    {
        "policy_disabled" =>
            "Review attention routing and notification policy for the integration.",
        "no_route_configured" or "no_recipients_configured" =>
            "Add subscriptions, operational team members, or attention routing recipients.",
        "duplicate_activation_suppressed" => null,
        "resolved_notification_disabled" =>
            "Enable resolved notifications in attention routing if desired.",
        "no_prior_activation_for_resolved" => null,
        "smtp_not_configured" or "smtp_from_not_configured" =>
            "Review SMTP settings under Platform settings (Notifications.Email).",
        _ => null,
    };
}
