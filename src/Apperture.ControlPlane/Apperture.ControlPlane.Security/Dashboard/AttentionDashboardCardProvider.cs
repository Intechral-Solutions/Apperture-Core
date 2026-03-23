using Apperture.ControlPlane.Dashboard;

namespace Apperture.ControlPlane.Security;

public sealed class AttentionDashboardCardProvider : IDashboardCardProvider
{
    private readonly IControlAttentionQueryService _attention;

    public AttentionDashboardCardProvider(IControlAttentionQueryService attention) => _attention = attention;

    public int Order => 20;

    public async ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        var items = await _attention.ListAttentionAsync(DateTimeOffset.UtcNow.AddHours(-24), 20, cancellationToken).ConfigureAwait(false);
        var rawSeverity = DashboardCardAttentionConvention.MaxAttentionSeverityToken(items.Select(i => i.Severity));
        var attention = DashboardCardAttentionConvention.FromAttentionSeverityToken(rawSeverity);

        return new DashboardCard(
            Id: "needs-attention",
            Type: "summary",
            Title: "Needs Attention",
            Order: Order,
            NavigationHref: "/control/center",
            Attention: attention,
            Summary: items.Count == 0 ? "No urgent items" : $"{items.Count} item(s) need review",
            Facts:
            [
                new DashboardCardFact("Open attention items", items.Count.ToString()),
                new DashboardCardFact("Highest severity", FormatAttentionSeverity(rawSeverity))
            ],
            NavigationLabel: "Open control center");
    }

    private static string FormatAttentionSeverity(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Equals("none", StringComparison.OrdinalIgnoreCase))
            return "None";
        return raw.ToLowerInvariant() switch
        {
            "high" => "High",
            "medium" => "Medium",
            "low" => "Low",
            _ => char.ToUpperInvariant(raw[0]) + (raw.Length > 1 ? raw[1..].ToLowerInvariant() : "")
        };
    }
}
