namespace Apperture.ModuleSdk.Abstractions;

/// <summary>
/// Optional module menu contributions. Hosts attach items only to known product rail
/// domains (Control Center, Integrations, Business Data, Config). Arbitrary new rail
/// domains are ignored.
/// </summary>
public interface IModuleNavigationProvider
{
    IReadOnlyList<NavigationContributionDescriptor> GetNavigationContributions();
}
