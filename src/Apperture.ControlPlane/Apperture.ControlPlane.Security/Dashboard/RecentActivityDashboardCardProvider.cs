using Apperture.ControlPlane.Dashboard;
using Apperture.Core.Application.ControlFacade;

namespace Apperture.ControlPlane.Security;

public sealed class RecentActivityDashboardCardProvider : IDashboardCardProvider
{
    private readonly IControlActivityQueryService _activity;

    public RecentActivityDashboardCardProvider(IControlActivityQueryService activity) => _activity = activity;

    public int Order => 30;

    public async ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        var items = await _activity.ListRecentActivityAsync(DateTimeOffset.UtcNow.AddHours(-24), 25, cancellationToken).ConfigureAwait(false);
        var latest = items.FirstOrDefault();
        return new DashboardCard(
            Id: "recent-activity",
            Type: "summary",
            Title: "Recent Activity",
            Order: Order,
            NavigationHref: "/control/center",
            Attention: DashboardCardAttention.None,
            Summary: latest is null
                ? "No activity in the last 24 hours"
                : $"Latest: {latest.OutcomeSummary}",
            Facts:
            [
                new DashboardCardFact("Activity in last 24h", items.Count.ToString()),
                new DashboardCardFact("Latest status", FormatActivityStatus(latest?.CurrentNormalizedStatus))
            ],
            NavigationLabel: "View control center");
    }

    private static string FormatActivityStatus(string? normalized) => string.IsNullOrWhiteSpace(normalized) || normalized == "none"
        ? "None"
        : char.ToUpperInvariant(normalized[0]) + normalized[1..].ToLowerInvariant();
}
