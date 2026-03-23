using Apperture.ControlPlane.Dashboard;
using Apperture.Core.Application.Services;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>Epic 46: additional real cards for the shared default operator dashboard. Epic 47: scoped queries + drill-in alignment.</summary>
public sealed class ExecutionsDashboardCardProvider : IDashboardCardProvider
{
    private readonly IRunQueryService _runs;
    private readonly IPrincipalContext _principalContext;

    public ExecutionsDashboardCardProvider(IRunQueryService runs, IPrincipalContext principalContext)
    {
        _runs = runs;
        _principalContext = principalContext;
    }

    public int Order => 25;

    public async ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return null;

        var from = DateTimeOffset.UtcNow.AddHours(-24);
        var failedRuns = await _runs.ListRunsAsync(
            new RunQueryFilter(Status: WorkflowLifecycleStatusNames.Failed, CreatedFrom: from),
            cancellationToken).ConfigureAwait(false);
        var windowRuns = await _runs.ListRunsAsync(new RunQueryFilter(CreatedFrom: from), cancellationToken).ConfigureAwait(false);

        var failed = failedRuns.Count;
        var running = 0;
        var completed = 0;
        var cancelled = 0;
        var other = 0;
        foreach (var r in windowRuns)
        {
            var s = r.Status ?? "";
            if (string.Equals(s, WorkflowLifecycleStatusNames.Failed, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(s, WorkflowLifecycleStatusNames.Running, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, WorkflowLifecycleStatusNames.Parked, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, WorkflowLifecycleStatusNames.Requested, StringComparison.OrdinalIgnoreCase))
                running++;
            else if (string.Equals(s, WorkflowLifecycleStatusNames.Completed, StringComparison.OrdinalIgnoreCase))
                completed++;
            else if (string.Equals(s, WorkflowLifecycleStatusNames.Cancelled, StringComparison.OrdinalIgnoreCase))
                cancelled++;
            else
                other++;
        }

        var visibleTotal = windowRuns.Count;
        var attention = DashboardCardAttentionConvention.FromVisibleFailedRunsCount(failed);
        var fromQuery = Uri.EscapeDataString(from.ToString("o"));
        var href = failed > 0
            ? $"/runs?status={Uri.EscapeDataString(WorkflowLifecycleStatusNames.Failed)}&createdFrom={fromQuery}"
            : $"/runs?createdFrom={fromQuery}";

        return new DashboardCard(
            Id: "executions-24h",
            Type: "summary",
            Title: "Executions (24h)",
            Order: Order,
            NavigationHref: href,
            Attention: attention,
            Summary: visibleTotal == 0
                ? "No executions visible to you in the last 24 hours"
                : $"{visibleTotal} execution(s) visible to you in the last 24 hours",
            Facts:
            [
                new DashboardCardFact("Visible in window", visibleTotal.ToString()),
                new DashboardCardFact("Failed", failed.ToString()),
                new DashboardCardFact("In progress", running.ToString()),
                new DashboardCardFact("Completed", completed.ToString()),
                new DashboardCardFact("Cancelled", cancelled.ToString()),
                new DashboardCardFact("Other statuses", other.ToString()),
                new DashboardCardFact("Data scope", "Executions you can view")
            ],
            NavigationLabel: failed > 0 ? "View failed runs" : "View runs");
    }
}
