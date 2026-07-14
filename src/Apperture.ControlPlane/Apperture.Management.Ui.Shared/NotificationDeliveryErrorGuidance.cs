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
        if (ContainsAny(
                err,
                "does not support secure",
                "starttls",
                "ssl/tls",
                "secure connection",
                "secure channel",
                "transport stream",
                "certificate"))
            return "SMTP TLS/STARTTLS handshake failed. For local bridges on port 1025, try turning SMTP use SSL/TLS off if the bridge does not offer STARTTLS; otherwise verify host/port match the bridge.";
        if (ContainsAny(err, "timeout", "timed out", "connection", "connect", "actively refused", "no connection"))
            return "Could not reach the SMTP server. Verify host, port, SSL, and network access.";
        if (ContainsAny(err, "recipient", "mailbox", "550", "553", "554"))
            return "The mail server rejected the recipient address. Verify the recipient email.";
        if (ContainsAny(err, "failure sending mail"))
            return "SMTP send failed with a generic client error. Check Management Host logs for the inner exception (search for \"SMTP send failed\"), or retry after rebuilding so LastError includes the full exception chain.";

        return "Review SMTP platform settings and Management Host logs (search for \"SMTP send failed\"). Credentials are not shown here.";
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
