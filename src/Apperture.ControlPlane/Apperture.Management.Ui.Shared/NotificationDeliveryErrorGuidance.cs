namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Operator guidance for outbox delivery errors without exposing SMTP secrets.
/// </summary>
public static class NotificationDeliveryErrorGuidance
{
    public static string? Summarize(string? lastError, string? outboxStatus)
    {
        if (string.IsNullOrWhiteSpace(lastError))
        {
            return outboxStatus is "failed"
                ? "Delivery failed. Check SMTP configuration and platform logs."
                : null;
        }

        var err = lastError.Trim();
        if (ContainsAny(err, "smtp_not_configured", "not configured", "SmtpHost"))
            return "SMTP is not configured. Set Notifications.Email platform settings.";
        if (ContainsAny(err, "smtp_from_not_configured", "FromAddress", "from address"))
            return "SMTP from address is missing. Set Notifications.Email.FromAddress.";
        if (ContainsAny(err, "authentication", "535", "credential", "password", "login"))
            return "SMTP authentication failed. Verify SMTP user and password secret reference.";
        if (ContainsAny(err, "timeout", "timed out", "connection", "connect"))
            return "Could not reach the SMTP server. Verify host, port, SSL, and network access.";
        if (ContainsAny(err, "recipient", "mailbox", "550", "553", "554"))
            return "The mail server rejected the recipient address. Verify the recipient email.";

        return "Review SMTP platform settings and server logs. Credentials are not shown here.";
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
