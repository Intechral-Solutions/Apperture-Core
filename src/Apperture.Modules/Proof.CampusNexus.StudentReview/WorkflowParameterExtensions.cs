using System.Text.Json;

namespace Proof.CampusNexus.StudentReview;

internal static class WorkflowParameterExtensions
{
    public static string? ReadString(this IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var v) || v is null)
            return null;
        return v switch
        {
            JsonElement je => je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => je.ToString()
            },
            string s => s,
            _ => v.ToString()
        };
    }

    public static int ReadInt(this IReadOnlyDictionary<string, object?> parameters, string key, int defaultValue)
    {
        var s = parameters.ReadString(key);
        if (string.IsNullOrWhiteSpace(s))
            return defaultValue;
        return int.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : defaultValue;
    }
}
