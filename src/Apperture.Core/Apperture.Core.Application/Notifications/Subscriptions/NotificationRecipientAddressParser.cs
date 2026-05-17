using System.Net.Mail;

namespace Apperture.Core.Application.Notifications.Subscriptions;

public sealed record ParsedNotificationRecipientAddress(string? DisplayName, string Email);

public sealed class NotificationRecipientAddressParser
{
    public bool TryParse(string input, out ParsedNotificationRecipientAddress? parsed, out string? error)
    {
        parsed = null;
        error = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Recipient address is required.";
            return false;
        }

        var trimmed = input.Trim();
        if (trimmed.Contains('\n') || trimmed.Contains('\r'))
        {
            error = "Recipient address must not contain newlines.";
            return false;
        }

        if (!trimmed.Contains('<', StringComparison.Ordinal))
        {
            if (!TryParseBareEmail(trimmed, out var email, out error))
                return false;
            parsed = new ParsedNotificationRecipientAddress(null, email);
            return true;
        }

        try
        {
            var addr = new MailAddress(trimmed);
            if (addr.DisplayName.Contains('>', StringComparison.Ordinal)
                || addr.DisplayName.Contains('\n', StringComparison.Ordinal)
                || addr.DisplayName.Contains('\r', StringComparison.Ordinal))
            {
                error = "Display name contains invalid characters.";
                return false;
            }

            if (!TryParseBareEmail(addr.Address, out var email, out error))
                return false;

            var displayName = string.IsNullOrWhiteSpace(addr.DisplayName) ? null : addr.DisplayName.Trim();
            parsed = new ParsedNotificationRecipientAddress(displayName, email);
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryParseBareEmail(string value, out string email, out string? error)
    {
        error = null;
        email = value.Trim().ToLowerInvariant();
        if (email.Length == 0)
        {
            error = "Email address is required.";
            return false;
        }

        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
