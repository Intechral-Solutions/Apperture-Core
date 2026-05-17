using System.Security.Cryptography;
using System.Text;

namespace Apperture.Core.Application.Notifications;

public sealed class NotificationTemplateRenderer : INotificationTemplateRenderer
{
    public RenderedNotificationSnapshot Render(
        NotificationTemplateDocument template,
        NotificationRenderContext context,
        string? variantKey = null)
    {
        var source = template;
        if (!string.IsNullOrWhiteSpace(variantKey) &&
            template.Variants is not null &&
            template.Variants.TryGetValue(variantKey, out var variant) &&
            variant is not null)
        {
            source = MergeVariant(template, variant);
        }

        return new RenderedNotificationSnapshot(
            Replace(source.Subject, context),
            Replace(source.Summary, context),
            Replace(source.BodyText, context),
            string.IsNullOrWhiteSpace(source.BodyHtml) ? null : Replace(source.BodyHtml, context),
            string.IsNullOrWhiteSpace(source.ActionText) ? null : Replace(source.ActionText, context),
            string.IsNullOrWhiteSpace(source.ActionUrl) ? null : Replace(source.ActionUrl, context),
            source.ContentFields,
            ComputeHash(template));
    }

    private static NotificationTemplateDocument MergeVariant(
        NotificationTemplateDocument root,
        NotificationTemplateVariant variant) =>
        new()
        {
            SchemaVersion = root.SchemaVersion,
            Subject = variant.Subject ?? root.Subject,
            Summary = variant.Summary ?? root.Summary,
            BodyText = variant.BodyText ?? root.BodyText,
            BodyHtml = variant.BodyHtml ?? root.BodyHtml,
            ActionText = variant.ActionText ?? root.ActionText,
            ActionUrl = variant.ActionUrl ?? root.ActionUrl,
            ContentFields = variant.ContentFields ?? root.ContentFields,
            Variants = root.Variants,
        };

    private static string Replace(string? template, NotificationRenderContext context)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        return System.Text.RegularExpressions.Regex.Replace(
            template,
            @"\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}",
            m =>
            {
                var key = m.Groups[1].Value;
                return context.TryGetValue(key, out var value) ? value : string.Empty;
            });
    }

    private static string ComputeHash(NotificationTemplateDocument template)
    {
        var bytes = Encoding.UTF8.GetBytes(NotificationDocumentJson.SerializeTemplate(template));
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }
}
