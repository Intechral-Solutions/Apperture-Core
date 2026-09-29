namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Distinguishes a real http(s) navigation URI from an application path.
/// A path that starts with <c>/</c> must stay a path: on Unix, <c>Uri.TryCreate</c> with
/// <see cref="UriKind.Absolute"/> accepts it as a <c>file</c> URI.
/// </summary>
internal static class AppNavigationUri
{
    internal static bool TryGetHttpAbsolute(string raw, out Uri absolute)
    {
        absolute = null!;
        if (raw.IndexOf("://", StringComparison.Ordinal) <= 0)
            return false;

        return Uri.TryCreate(raw, UriKind.Absolute, out absolute!)
            && absolute.Scheme is "http" or "https";
    }
}
