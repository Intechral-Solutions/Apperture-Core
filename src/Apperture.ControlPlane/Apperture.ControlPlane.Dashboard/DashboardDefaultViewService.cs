using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Dashboard;

/// <summary>
/// Composes the shared default dashboard from <see cref="IDashboardCardProvider"/> contributions.
/// Provider failures are isolated: one bad provider does not fail the whole dashboard.
/// </summary>
public sealed class DashboardDefaultViewService : IDashboardDefaultViewService
{
    public const string SharedPlatformOperatorScope = "shared-platform-operator";
    public const string DefaultTitle = "Platform Summary";

    private readonly IEnumerable<IDashboardCardProvider> _providers;
    private readonly ILogger<DashboardDefaultViewService> _logger;

    public DashboardDefaultViewService(
        IEnumerable<IDashboardCardProvider> providers,
        ILogger<DashboardDefaultViewService> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public async ValueTask<DashboardDefaultView> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        var cards = new List<DashboardCard>();
        var failureCount = 0;

        foreach (var provider in _providers.OrderBy(p => p.Order))
        {
            var providerName = provider.GetType().Name;
            try
            {
                var card = await provider.BuildAsync(cancellationToken).ConfigureAwait(false);
                if (card is not null)
                    cards.Add(card);
            }
            catch (Exception ex)
            {
                failureCount++;
                _logger.LogWarning(
                    ex,
                    "Dashboard card provider {ProviderName} failed; continuing with other cards.",
                    providerName);
            }
        }

        if (failureCount > 0)
        {
            cards.Add(new DashboardCard(
                Id: "dashboard-partial-load",
                Type: "system",
                Title: "Partial summary load",
                Order: 999,
                NavigationHref: null,
                Attention: DashboardCardAttention.Warning,
                Summary: "One or more summary cards failed to load. The cards above are still valid.",
                Facts: [new DashboardCardFact("Sources that failed", failureCount.ToString())],
                IsPlaceholder: true,
                PlaceholderReason: "Provider threw an exception during build."));
        }

        return new DashboardDefaultView(
            Scope: SharedPlatformOperatorScope,
            Title: DefaultTitle,
            Cards: cards,
            ProviderFailureCount: failureCount);
    }
}
