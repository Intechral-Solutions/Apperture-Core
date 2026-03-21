namespace Apperture.ModuleSdk.Abstractions;

public interface IModuleNavigationProvider
{
    IReadOnlyList<NavigationContributionDescriptor> GetNavigationContributions();
}
