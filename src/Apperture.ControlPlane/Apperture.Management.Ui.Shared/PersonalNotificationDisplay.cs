using System.Text.RegularExpressions;

namespace Apperture.Management.Ui.Shared;

/// <summary>Compact copy for personal inbox UI (Epic 102).</summary>
public static partial class PersonalNotificationDisplay
{
    [GeneratedRegex(@"^\[Apperture\]\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AppertureSubjectPrefix();

    [GeneratedRegex(@"\s*\([^)]+\)\s*$")]
    private static partial Regex TrailingParenthetical();

    /// <summary>Primary line for the top-bar menu (prefer human summary over email-style subject).</summary>
    public static string GetMenuTitle(string? subject, string? summary)
    {
        if (!string.IsNullOrWhiteSpace(summary))
            return summary.Trim();

        return CompactSubject(subject);
    }

    public static string CompactSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return "Notification";

        var text = AppertureSubjectPrefix().Replace(subject.Trim(), "");
        text = TrailingParenthetical().Replace(text, "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            return "Notification";

        return HumanizeUnderscores(text);
    }

    private static string HumanizeUnderscores(string text)
    {
        var colon = text.IndexOf(':');
        if (colon < 0)
            return text.Replace('_', ' ');

        var label = text[..(colon + 1)];
        var detail = text[(colon + 1)..].Trim().Replace('_', ' ');
        return string.IsNullOrEmpty(detail) ? label.TrimEnd(':') : $"{label} {detail}";
    }
}
