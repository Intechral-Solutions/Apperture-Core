using System.Text.RegularExpressions;
using Apperture.Core.Domain.Notifications;

namespace Apperture.Core.Application.Notifications;

public sealed partial class NotificationTemplateValidator : INotificationTemplateValidator
{
    public const int MaxTemplateFieldLength = 16_384;

    private readonly INotificationTemplateVariableCatalog _variables;
    private readonly IRedactionPolicy _redaction;

    public NotificationTemplateValidator(
        INotificationTemplateVariableCatalog variables,
        IRedactionPolicy redaction)
    {
        _variables = variables;
        _redaction = redaction;
    }

    public NotificationValidationResult Validate(string templateJson, NotificationTemplateValidationContext context)
    {
        if (!NotificationDocumentJson.TryDeserializeTemplate(templateJson, out var document, out var parseError))
            return NotificationValidationResult.Failure(new NotificationValidationIssue("parse", parseError ?? "Invalid template JSON."));

        if (document!.SchemaVersion != NotificationSchemaVersions.TemplateV1)
            return NotificationValidationResult.Failure(
                new NotificationValidationIssue("schemaVersion", $"schemaVersion must be {NotificationSchemaVersions.TemplateV1}."));

        var eventTypes = context.EventTypes ?? [];
        var allowlist = eventTypes.Count > 0
            ? _variables.GetUnionAllowlist(eventTypes)
            : new HashSet<string>(StringComparer.Ordinal);

        var issues = new List<NotificationValidationIssue>();
        ValidateBaseTemplate(document, allowlist, "template", issues);
        if (document.Variants is not null)
        {
            foreach (var (name, variant) in document.Variants)
                ValidateVariant(variant, allowlist, $"variants.{name}", issues);
        }

        return issues.Count == 0
            ? NotificationValidationResult.Success()
            : new NotificationValidationResult(false, issues);
    }

    private void ValidateBaseTemplate(
        NotificationTemplateDocument document,
        IReadOnlySet<string> allowlist,
        string pathPrefix,
        List<NotificationValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(document.Subject))
            issues.Add(new($"{pathPrefix}.subject", "subject is required."));
        if (string.IsNullOrWhiteSpace(document.Summary))
            issues.Add(new($"{pathPrefix}.summary", "summary is required."));
        if (string.IsNullOrWhiteSpace(document.BodyText))
            issues.Add(new($"{pathPrefix}.bodyText", "bodyText is required."));

        ValidateString(document.Subject, $"{pathPrefix}.subject", allowlist, issues);
        ValidateString(document.Summary, $"{pathPrefix}.summary", allowlist, issues);
        ValidateString(document.BodyText, $"{pathPrefix}.bodyText", allowlist, issues);
        ValidateString(document.BodyHtml, $"{pathPrefix}.bodyHtml", allowlist, issues);
        ValidateString(document.ActionText, $"{pathPrefix}.actionText", allowlist, issues);
        ValidateActionUrl(document.ActionUrl, $"{pathPrefix}.actionUrl", allowlist, issues);

        if (!string.IsNullOrWhiteSpace(document.BodyHtml) && !_redaction.AllowHtmlBody(document))
            issues.Add(new($"{pathPrefix}.bodyHtml", "HTML body is not allowed for this template."));
    }

    private void ValidateVariant(
        NotificationTemplateVariant? variant,
        IReadOnlySet<string> allowlist,
        string pathPrefix,
        List<NotificationValidationIssue> issues)
    {
        if (variant is null)
            return;

        ValidateString(variant.Subject, $"{pathPrefix}.subject", allowlist, issues);
        ValidateString(variant.Summary, $"{pathPrefix}.summary", allowlist, issues);
        ValidateString(variant.BodyText, $"{pathPrefix}.bodyText", allowlist, issues);
        ValidateString(variant.BodyHtml, $"{pathPrefix}.bodyHtml", allowlist, issues);
        ValidateString(variant.ActionText, $"{pathPrefix}.actionText", allowlist, issues);
        ValidateActionUrl(variant.ActionUrl, $"{pathPrefix}.actionUrl", allowlist, issues);
    }

    private static void ValidateString(
        string? value,
        string path,
        IReadOnlySet<string> allowlist,
        List<NotificationValidationIssue> issues)
    {
        if (string.IsNullOrEmpty(value))
            return;
        if (value.Length > MaxTemplateFieldLength)
            issues.Add(new(path, $"Exceeds max length {MaxTemplateFieldLength}."));
        foreach (var token in ExtractTokens(value))
        {
            if (!IsTokenSyntaxValid(token))
                issues.Add(new(path, $"Invalid template token '{{{{{token}}}}}'."));
            else if (allowlist.Count > 0 && !allowlist.Contains(token))
                issues.Add(new(path, $"Variable '{token}' is not allowlisted."));
        }
    }

    private static void ValidateActionUrl(
        string? actionUrl,
        string path,
        IReadOnlySet<string> allowlist,
        List<NotificationValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(actionUrl))
            return;

        ValidateString(actionUrl, path, allowlist, issues);
        if (actionUrl.Contains("{{", StringComparison.Ordinal))
            return;

        if (actionUrl.StartsWith("/", StringComparison.Ordinal))
            return;
        if (actionUrl.StartsWith("{{instance.baseUrl}}", StringComparison.Ordinal))
            return;

        issues.Add(new(path, "actionUrl must be relative (/...) or start with {{instance.baseUrl}}."));
    }

    internal static IEnumerable<string> ExtractTokens(string template)
    {
        foreach (Match match in TokenRegex().Matches(template))
            yield return match.Groups[1].Value;
    }

    internal static bool IsTokenSyntaxValid(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;
        if (token.Contains("..", StringComparison.Ordinal))
            return false;
        if (token.StartsWith('_'))
            return false;
        foreach (var segment in token.Split('.'))
        {
            if (segment.Length == 0 || !segment.All(c => char.IsLetterOrDigit(c) || c == '_'))
                return false;
        }

        return true;
    }

    [GeneratedRegex(@"\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
