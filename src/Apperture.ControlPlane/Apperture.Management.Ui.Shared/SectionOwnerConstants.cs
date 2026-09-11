namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Section owner identities for the shell navigation model.
/// Control Center (top logo) and Config (bottom gear) are custom-rendered zone owners.
/// Middle domains are rendered through the dynamic domain loop.
/// </summary>
public static class SectionOwnerConstants
{
    /// <summary>Control Center / operator home; owned by the top logo.</summary>
    public const string ControlCenter = "Control Center";

    /// <summary>Compatibility alias for pre-NAV-1 tests and leftover Dashboard strings.</summary>
    public const string Dashboard = ControlCenter;

    /// <summary>Config / admin section; owned by the bottom gear icon.</summary>
    public const string Config = "Config";

    /// <summary>First-class Integrations rail domain.</summary>
    public const string Integrations = "Integrations";

    /// <summary>First-class Business Data rail domain.</summary>
    public const string BusinessData = "Business Data";

    /// <summary>Domains that must not appear in the middle dynamic loop (they have custom rail entries).</summary>
    public static readonly IReadOnlySet<string> CustomZoneOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ControlCenter,
        Config
    };

    /// <summary>
    /// Rail domains modules may attach contributions to. Contributions naming any other domain are ignored.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownRailDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ControlCenter,
        Integrations,
        BusinessData,
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
