namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Operator-facing labels and troubleshooting guidance for recipient skip reasons
/// stored in notification instance recipient snapshots.
/// </summary>
public static class NotificationRecipientSkipReasonLabels
{
    public static string Label(string? reason) => reason switch
    {
        "userOptOut" => "User opted out of email for this notification",
        "missingEmail" => "Recipient does not have an email address",
        "noEmailSubscription" => "Email subscription is not enabled",
        "emailOptedOut" => "Email delivery is disabled by preference",
        "deliveryModeMuted" => "Subscription delivery mode is muted",
        "deliveryModeDigestOnly" => "Subscription is digest-only (immediate email deferred)",
        "contactDisabled" => "Managed contact is disabled",
        "userDisabled" => "User account is disabled",
        "teamDisabled" => "Operational team is disabled",
        "teamEmpty" => "Operational team has no members",
        "duplicateEmail" => "Duplicate email address (already a deliverable recipient)",
        _ => string.IsNullOrWhiteSpace(reason)
            ? "Skipped (reason not recorded)"
            : "Skipped for an unrecognized reason",
    };

    public static string? HelpText(string? reason) => reason switch
    {
        "userOptOut" =>
            "The user has a global email opt-out for notifications. In-app inbox items may still appear when routing applies.",
        "missingEmail" =>
            "Add an email address to the user profile or managed contact, or route through a team member who has email.",
        "noEmailSubscription" =>
            "The user has not enabled email for this notification under My notifications, and no admin or team subscription applies.",
        "emailOptedOut" =>
            "The user turned off email for this notification under My notifications.",
        "deliveryModeMuted" =>
            "An admin assigned this subscription with delivery mode muted. Change the subscription delivery mode on the notification definition.",
        "deliveryModeDigestOnly" =>
            "Immediate email is not sent for digest-only subscriptions. Daily digest delivery is not available yet.",
        "contactDisabled" =>
            "Re-enable the managed contact or update the subscription to use an active contact.",
        "userDisabled" =>
            "The linked user account is disabled. Enable the account or update routing.",
        "teamDisabled" =>
            "Re-enable the operational team or assign recipients through another subscription.",
        "teamEmpty" =>
            "Add members to the operational team or assign direct subscriptions.",
        "duplicateEmail" =>
            "The same email was already included from another subscription or routing path.",
        _ => null,
    };

    public static string? ActionGuidance(string? reason) => reason switch
    {
        "userOptOut" or "emailOptedOut" =>
            "Ask the user to review email preferences under My notifications, or remove their global opt-out in notification subscriptions.",
        "missingEmail" =>
            "Add an email address to the user or contact record.",
        "noEmailSubscription" =>
            "Ask the user to enable email under My notifications, or add an admin subscription on the notification definition.",
        "deliveryModeMuted" or "deliveryModeDigestOnly" =>
            "Review subscription delivery mode on the notification definition.",
        "contactDisabled" =>
            "Review managed contacts and subscription assignments.",
        "userDisabled" =>
            "Review user account status in directory settings.",
        "teamDisabled" or "teamEmpty" =>
            "Review operational team membership and status.",
        "duplicateEmail" => null,
        _ => reason is { Length: > 0 }
            ? "Review notification definition subscriptions and recipient routing."
            : null,
    };

    public static bool IsRecognized(string? reason) => reason switch
    {
        "userOptOut" or "missingEmail" or "noEmailSubscription" or "emailOptedOut"
            or "deliveryModeMuted" or "deliveryModeDigestOnly" or "contactDisabled"
            or "userDisabled" or "teamDisabled" or "teamEmpty" or "duplicateEmail" => true,
        _ => false,
    };
}
