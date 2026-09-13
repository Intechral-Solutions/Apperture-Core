namespace Apperture.Management.Ui.Shared;

/// <summary>
/// A timezone offered in the account timezone selector.
/// </summary>
/// <param name="Id">
/// Canonical identifier, preferring the IANA form. This is the value saved on the account, and it is
/// also what browsers report, so detection and manual selection agree.
/// </param>
/// <param name="DisplayName">
/// The host's description of the zone. Included as secondary search text because it names cities that
/// the identifier may not.
/// </param>
public sealed record OperatorTimeZoneChoice(string Id, string DisplayName, TimeSpan BaseUtcOffset)
{
    /// <summary>
    /// Label including the UTC offset so zones at the same offset are still distinguishable. The
    /// identifier leads because it is the unambiguous, cross-platform name and the value being stored.
    /// </summary>
    public string Label
    {
        get
        {
            var sign = BaseUtcOffset < TimeSpan.Zero ? "-" : "+";
            var magnitude = BaseUtcOffset.Duration();
            return $"{Id} (UTC{sign}{magnitude:hh\\:mm})";
        }
    }

    /// <summary>Free-text match across the identifier and the host description.</summary>
    public bool Matches(string needle) =>
        Id.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase)
        // "America/Los_Angeles" should be findable by typing "Los Angeles".
        || Id.Replace('_', ' ').Contains(needle, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Builds the timezone list for the account selector from the zones the host actually recognizes, so
/// the UI can never offer an identifier the server would reject.
/// </summary>
public static class OperatorTimeZoneCatalog
{
    /// <summary>
    /// Zones surfaced first because they cover the deployments Apperture runs in today. Everything the
    /// host knows about is still selectable through the search box below these.
    /// </summary>
    private static readonly string[] PreferredIds =
    [
        "UTC",
        "Africa/Johannesburg",
        "Europe/London",
        "Europe/Amsterdam",
        "America/New_York",
        "America/Chicago",
        "America/Denver",
        "America/Los_Angeles",
        "Asia/Dubai",
        "Asia/Kolkata",
        "Asia/Singapore",
        "Australia/Sydney",
        "Pacific/Auckland",
    ];

    private static readonly Lazy<IReadOnlyList<OperatorTimeZoneChoice>> AllChoices =
        new(BuildAll, isThreadSafe: true);

    private static readonly Lazy<IReadOnlyList<OperatorTimeZoneChoice>> SuggestedChoices =
        new(BuildSuggested, isThreadSafe: true);

    /// <summary>Every timezone the host recognizes, ordered by offset then name.</summary>
    public static IReadOnlyList<OperatorTimeZoneChoice> All => AllChoices.Value;

    /// <summary>The short curated list shown before the operator searches.</summary>
    public static IReadOnlyList<OperatorTimeZoneChoice> Suggested => SuggestedChoices.Value;

    /// <summary>
    /// Filters the full list by a free-text query. An empty query returns the curated suggestions so
    /// the selector opens with something short and useful rather than several hundred rows.
    /// </summary>
    public static IReadOnlyList<OperatorTimeZoneChoice> Search(string? query, int take = 40)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Suggested;

        var needle = query.Trim();
        return All.Where(c => c.Matches(needle)).Take(take).ToList();
    }

    public static bool IsKnown(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId)
        && OperatorTimestampFormat.TryFind(timeZoneId, out _);

    /// <summary>Resolves a display label for an id, falling back to the raw id when unrecognized.</summary>
    public static string DescribeId(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return "Follow this browser";

        var trimmed = timeZoneId.Trim();
        var match = All.FirstOrDefault(c => string.Equals(c.Id, trimmed, StringComparison.OrdinalIgnoreCase));
        return match?.Label ?? trimmed;
    }

    /// <summary>
    /// Enumerates the host's zones and canonicalizes each identifier to its IANA form.
    /// </summary>
    /// <remarks>
    /// This matters on Windows, where <see cref="TimeZoneInfo.GetSystemTimeZones"/> yields Windows
    /// identifiers such as "South Africa Standard Time". Browsers report IANA identifiers, and
    /// Apperture's schedule model already prefers IANA, so offering the Windows form would produce a
    /// stored value that disagrees with browser detection and reads inconsistently against schedules.
    /// Canonicalizing here keeps one identifier style across detection, storage, and display on every
    /// host operating system.
    /// </remarks>
    private static IReadOnlyList<OperatorTimeZoneChoice> BuildAll()
    {
        var byId = new Dictionary<string, OperatorTimeZoneChoice>(StringComparer.OrdinalIgnoreCase);

        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            var id = CanonicalId(zone);

            // Only offer an identifier the host can resolve, so a selection can never be rejected
            // by the server that produced this list.
            if (!OperatorTimestampFormat.TryFind(id, out _))
                continue;

            var display = string.IsNullOrWhiteSpace(zone.DisplayName) ? id : zone.DisplayName;
            byId.TryAdd(id, new OperatorTimeZoneChoice(id, display, zone.BaseUtcOffset));
        }

        byId.TryAdd(
            OperatorTimestampFormat.UtcId,
            new OperatorTimeZoneChoice(OperatorTimestampFormat.UtcId, "Coordinated Universal Time", TimeSpan.Zero));

        return byId.Values
            .OrderBy(c => c.BaseUtcOffset)
            .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string CanonicalId(TimeZoneInfo zone)
    {
        if (zone.HasIanaId)
            return zone.Id;

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var ianaId) && !string.IsNullOrWhiteSpace(ianaId)
            ? ianaId
            : zone.Id;
    }

    private static IReadOnlyList<OperatorTimeZoneChoice> BuildSuggested()
    {
        var byId = All.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var suggested = new List<OperatorTimeZoneChoice>();
        foreach (var id in PreferredIds)
        {
            if (byId.TryGetValue(id, out var choice))
                suggested.Add(choice);
        }

        return suggested.Count > 0 ? suggested : All.Take(20).ToList();
    }
}
