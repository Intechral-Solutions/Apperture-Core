using System.Net.Mail;

namespace Apperture.ControlPlane.Security;

public static class NotificationSmtpMessageBuilder
{
    public static void ApplyBodies(MailMessage message, string plainTextBody, string? htmlBody)
    {
        if (!string.IsNullOrWhiteSpace(htmlBody))
        {
            message.Body = plainTextBody;
            message.IsBodyHtml = false;
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(plainTextBody, null, "text/plain"));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, null, "text/html"));
            return;
        }

        message.Body = plainTextBody;
        message.IsBodyHtml = false;
    }
}
