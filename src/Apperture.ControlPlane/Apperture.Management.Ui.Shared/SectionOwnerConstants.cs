namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Section owner identities for the shell navigation model.
/// Dashboard (top logo) and Config (bottom gear) are custom-rendered zone owners.
/// Middle domains are rendered through the dynamic domain loop.
/// </summary>
public static class SectionOwnerConstants
{
    /// <summary>Dashboard / platform-home section; owned by the top logo icon.</summary>
    public const string Dashboard = "Dashboard";

    /// <summary>Config / admin section; owned by the bottom gear icon.</summary>
    public const string Config = "Config";

    /// <summary>Domains that must not appear in the middle dynamic loop (they have custom rail entries).</summary>
    public static readonly IReadOnlySet<string> CustomZoneOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Dashboard,
        Config
    };

    /// <summary>Returns domain names that should be rendered in the middle dynamic rail.</summary>
    public static IEnumerable<string> GetMiddleDomains(IEnumerable<string> allDomains)
    {
        foreach (var d in allDomains)
        {
            if (!CustomZoneOwners.Contains(d))
                yield return d;
        }
    }
}
