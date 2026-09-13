using System.Net;
using System.Text;

namespace Apperture.Core.Application.Notifications.Delivery;

public sealed record NotificationEmailComposeInput(
    string Subject,
    string Summary,
    string BodyText,
    string? BodyHtml,
    string? ActionText,
    string? ActionUrl,
    string? InstanceBaseUrl,
    string? IntegrationDisplayName = null,
    string? IntegrationId = null,
    string? OccurredAtUtc = null);

public sealed record NotificationEmailMessage(
    string PlainTextBody,
    string? HtmlBody,
    string? AbsoluteActionUrl,
    string? EmailActionUrl);

public static class NotificationEmailMessageComposer
{
    private const string DefaultActionText = "Open in Apperture";
    private const string FooterText = "Apperture platform notification";

    public static NotificationEmailMessage Compose(NotificationEmailComposeInput input)
    {
        var normalized = NotificationEmailActionLinkNormalizer.Normalize(input.InstanceBaseUrl, input.ActionUrl);
        var emailActionUrl = normalized ?? input.ActionUrl?.Trim();
        var absoluteActionUrl = emailActionUrl is not null
            && NotificationEmailActionLinkNormalizer.IsAbsoluteHttpUrl(emailActionUrl)
            ? emailActionUrl
            : null;
        var actionLabel = string.IsNullOrWhiteSpace(input.ActionText) ? DefaultActionText : input.ActionText.Trim();

        var plainBody = BuildPlainTextBody(input.BodyText, actionLabel, emailActionUrl);
        var htmlBody = BuildHtmlBody(input, actionLabel, emailActionUrl);

        return new NotificationEmailMessage(plainBody, htmlBody, absoluteActionUrl, emailActionUrl);
    }

    private static string BuildPlainTextBody(string bodyText, string actionLabel, string? emailActionUrl)
    {
        var builder = new StringBuilder(bodyText?.TrimEnd() ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(emailActionUrl))
        {
            if (builder.Length > 0)
                builder.AppendLine().AppendLine();
            builder.Append(actionLabel).Append(": ").Append(emailActionUrl);
        }

        return builder.ToString();
    }

    private static string? BuildHtmlBody(NotificationEmailComposeInput input, string actionLabel, string? emailActionUrl)
    {
        var title = string.IsNullOrWhiteSpace(input.Subject) ? "Apperture notification" : input.Subject.Trim();
        var summary = input.Summary?.Trim();
        var body = string.IsNullOrWhiteSpace(input.BodyHtml) ? input.BodyText?.Trim() : input.BodyHtml.Trim();
        if (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(summary))
            return null;

        var integrationLine = BuildIntegrationLine(input.IntegrationDisplayName, input.IntegrationId);
        var timestampLine = string.IsNullOrWhiteSpace(input.OccurredAtUtc) ? null : $"{input.OccurredAtUtc.Trim()} UTC";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset=\"utf-8\" /></head>");
        sb.AppendLine("<body style=\"font-family:Segoe UI,Helvetica,Arial,sans-serif;color:#1f2937;line-height:1.5;margin:0;padding:24px;background:#f8fafc;\">");
        sb.AppendLine("<div style=\"max-width:640px;margin:0 auto;background:#ffffff;border:1px solid #e5e7eb;border-radius:8px;padding:24px;\">");
        sb.Append("<h1 style=\"margin:0 0 12px;font-size:20px;\">").Append(HtmlEncode(title)).AppendLine("</h1>");

        if (!string.IsNullOrWhiteSpace(summary))
            sb.Append("<p style=\"margin:0 0 16px;font-size:15px;\">").Append(HtmlEncode(summary)).AppendLine("</p>");

        if (!string.IsNullOrWhiteSpace(body))
        {
            if (string.IsNullOrWhiteSpace(input.BodyHtml))
            {
                sb.Append("<pre style=\"white-space:pre-wrap;font-family:inherit;font-size:14px;background:#f9fafb;padding:16px;border-radius:6px;border:1px solid #e5e7eb;margin:0 0 16px;\">")
                    .Append(HtmlEncode(body))
                    .AppendLine("</pre>");
            }
            else
            {
                sb.Append("<div style=\"font-size:14px;margin:0 0 16px;\">").Append(body).AppendLine("</div>");
            }
        }

        if (!string.IsNullOrWhiteSpace(emailActionUrl))
        {
            sb.Append("<p style=\"margin:0 0 16px;\">")
                .Append("<a href=\"")
                .Append(HtmlEncodeAttribute(emailActionUrl))
                .Append("\" style=\"display:inline-block;background:#2563eb;color:#ffffff;text-decoration:none;padding:10px 16px;border-radius:6px;font-weight:600;\">")
                .Append(HtmlEncode(actionLabel))
                .AppendLine("</a></p>");
        }

        if (!string.IsNullOrWhiteSpace(integrationLine))
            sb.Append("<p style=\"margin:0 0 8px;font-size:13px;color:#6b7280;\">").Append(HtmlEncode(integrationLine)).AppendLine("</p>");

        if (!string.IsNullOrWhiteSpace(timestampLine))
            sb.Append("<p style=\"margin:0 0 16px;font-size:13px;color:#6b7280;\">").Append(HtmlEncode(timestampLine)).AppendLine("</p>");

        sb.Append("<hr style=\"border:none;border-top:1px solid #e5e7eb;margin:16px 0;\" />");
        sb.Append("<p style=\"margin:0;font-size:12px;color:#9ca3af;\">").Append(HtmlEncode(FooterText)).AppendLine("</p>");
        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    private static string? BuildIntegrationLine(string? displayName, string? integrationId)
    {
        if (!string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(integrationId))
            return $"Integration: {displayName} ({integrationId})";
        if (!string.IsNullOrWhiteSpace(displayName))
            return $"Integration: {displayName}";
        if (!string.IsNullOrWhiteSpace(integrationId))
            return $"Integration: {integrationId}";
        return null;
    }

    private static string HtmlEncode(string value) => WebUtility.HtmlEncode(value);

    private static string HtmlEncodeAttribute(string value) => WebUtility.HtmlEncode(value);
}
