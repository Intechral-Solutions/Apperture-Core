using System.Text.RegularExpressions;

namespace Apperture.Core.Application.Notifications;

public static partial class NotificationIntervalParser
{
    [GeneratedRegex(@"^(?<value>\d+)(?<unit>m|h|d)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IntervalRegex();

    public static bool TryParse(string? text, out TimeSpan interval)
    {
        interval = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = IntervalRegex().Match(text.Trim());
        if (!match.Success || !int.TryParse(match.Groups["value"].Value, out var value))
            return false;

        if (value == 0)
        {
            interval = TimeSpan.Zero;
            return true;
        }

        interval = match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "m" => TimeSpan.FromMinutes(value),
            "h" => TimeSpan.FromHours(value),
            "d" => TimeSpan.FromDays(value),
            _ => default,
        };
        return interval > TimeSpan.Zero;
    }

    public static bool IsValidFormat(string? text) =>
        string.IsNullOrWhiteSpace(text) || TryParse(text, out _);
}
