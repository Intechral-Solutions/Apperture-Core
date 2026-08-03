using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>BDS-5 — Demo Customers business record surface conventions.</summary>
public sealed class Bds5DemoCustomersSurfaceTests
{
    [Fact]
    public void Routes_are_demo_scoped_not_final_records()
    {
        DemoCustomerUiRoutes.List.Should().Be("/demo/customers");
        DemoCustomerUiRoutes.Detail("C-100").Should().Be("/demo/customers/C-100");
        DeveloperTaskRoutes.DemoCustomers.Should().Be(DemoCustomerUiRoutes.List);

        DemoCustomerUiHelpers.PageTitle.Should().Be("Demo Customers");
        DemoCustomerUiHelpers.DemoScopeBanner.Should().Contain("demo projection");
        DemoCustomerUiHelpers.DemoScopeBanner.Should().Contain("BusinessRecord");
        DemoCustomerUiHelpers.PageTitle.Should().NotContain("Records");
    }

    [Fact]
    public void Primary_menu_does_not_advertise_demo_customers_as_final_records()
    {
        var hrefs = ControlPanelMenuDefinitions.GetDefaultMenus()
            .SelectMany(kv => Flatten(kv.Value.Items))
            .Select(i => i.Href)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        hrefs.Should().NotContain(DemoCustomerUiRoutes.List);
        hrefs.Should().NotContain(h => h!.Contains("/records", StringComparison.OrdinalIgnoreCase));
        hrefs.Should().Contain(ShellSectionRoutes.DeveloperTasks);
    }

    [Fact]
    public void DeveloperTasks_links_demo_customers_card()
    {
        var content = File.ReadAllText(RepoPath(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/DeveloperTasks.razor"));

        content.Should().Contain("Demo Customers");
        content.Should().Contain("DeveloperTaskRoutes.DemoCustomers");
        content.Should().Contain("demo projection");
        content.Should().NotContain("final Records");
    }

    [Fact]
    public void List_page_is_business_first_and_calls_client_filters()
    {
        var content = File.ReadAllText(RepoPath(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Demo/DemoCustomersList.razor"));

        content.Should().Contain("@page \"/demo/customers\"");
        content.Should().Contain("GetDemoCustomerRecordsAsync");
        content.Should().Contain("DemoCustomerUiHelpers.BuildListFilter");
        content.Should().Contain(">Customer<");
        content.Should().Contain(">Business key<");
        content.Should().Contain(">Email<");
        content.Should().Contain(">Status<");
        content.Should().Contain(">Sources<");
        content.Should().Contain(">Conflicts<");
        content.Should().Contain(">Last observed<");
        content.Should().Contain("PrimaryConflictSummary");
        content.Should().Contain("Conflicts only");
        content.Should().Contain("SourceKindOptions");
        content.Should().Contain("EmptyState");
        content.Should().Contain("ForbiddenState");
        content.Should().Contain("ErrorState");
        content.Should().Contain("DemoScopeBanner");
        content.Should().Contain("TimestampDisplay");
        content.Should().NotContain("ToLocalTime()");
        content.Should().Contain("CanViewIntegrations");
        content.Should().NotContain("operationItemId");
    }

    [Fact]
    public void Detail_page_keeps_platform_ids_behind_disclosure()
    {
        var content = File.ReadAllText(RepoPath(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Demo/DemoCustomerDetail.razor"));

        content.Should().Contain("@page \"/demo/customers/{BusinessKey}\"");
        content.Should().Contain("GetDemoCustomerRecordAsync");
        content.Should().Contain("Sources");
        content.Should().Contain("Conflicts");
        content.Should().Contain("SourceKindLabel");
        content.Should().Contain("ValuesBySource");
        content.Should().Contain("GetRunDetailHref");
        content.Should().Contain("TryGetIntegrationHref");
        content.Should().Contain("<details");
        content.Should().Contain("Platform identifiers");
        content.Should().Contain("operationItemId");
        content.Should().Contain("operationGroupId");
        content.IndexOf("Customer")
            .Should().BeLessThan(content.IndexOf("Platform identifiers"));
        content.Should().Contain("TimestampDisplay");
        content.Should().NotContain("ToLocalTime()");
        content.Should().Contain("DemoScopeBanner");
        content.Should().Contain("resolution is not implemented");
    }

    [Fact]
    public void Drill_through_targets_existing_run_and_integration_routes()
    {
        DemoCustomerUiHelpers.GetRunDetailHref("run-1").Should().Be("/runs/run-1");
        DemoCustomerUiHelpers.TryGetIntegrationHref("int-1").Should().Be(IntegrationUiRoutes.Detail("int-1"));
        DemoCustomerUiHelpers.TryGetIntegrationHref(null).Should().BeNull();
        DemoCustomerUiHelpers.TryGetIntegrationHref(" ").Should().BeNull();

        var detail = File.ReadAllText(RepoPath(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Demo/DemoCustomerDetail.razor"));
        detail.Should().Contain("Open run");
        detail.Should().Contain("GetRunDetailHref");
        detail.Should().Contain("Open integration");
        detail.Should().Contain("TryGetIntegrationHref");
        detail.Should().NotContain("/operation-items/");
    }

    [Fact]
    public void BuildListFilter_maps_search_source_conflict_and_paging()
    {
        var filter = DemoCustomerUiHelpers.BuildListFilter("Ada", "file_import", conflictsOnly: true, skip: 10, take: 25);
        filter.Q.Should().Be("Ada");
        filter.SourceKind.Should().Be("file_import");
        filter.HasConflict.Should().BeTrue();
        filter.Skip.Should().Be(10);
        filter.Take.Should().Be(25);

        var cleared = DemoCustomerUiHelpers.BuildListFilter("  ", "", conflictsOnly: false, skip: 0);
        cleared.Q.Should().BeNull();
        cleared.SourceKind.Should().BeNull();
        cleared.HasConflict.Should().BeNull();
    }

    [Fact]
    public void Empty_copy_distinguishes_no_data_search_and_conflicts()
    {
        var none = DemoCustomerUiHelpers.ResolveEmptyCopy(null, null, false);
        none.Title.Should().Be(DemoCustomerUiHelpers.EmptyTitle);
        none.Message.Should().Contain("FileImport");

        var search = DemoCustomerUiHelpers.ResolveEmptyCopy("Ada", null, false);
        search.Title.Should().Be(DemoCustomerUiHelpers.EmptySearchTitle);

        var conflicts = DemoCustomerUiHelpers.ResolveEmptyCopy(null, null, true);
        conflicts.Title.Should().Be(DemoCustomerUiHelpers.EmptyConflictsTitle);
    }

    [Fact]
    public void Conflict_helpers_prefer_summary_and_warning_semantic()
    {
        var conflicted = new DemoCustomerRecordDto(
            "customer", "C-100", "Valid One", "a@x", "Active", 2, true, 2,
            DateTimeOffset.UtcNow, [], [], "email differs across sources.");
        DemoCustomerUiHelpers.ConflictLabel(conflicted).Should().Contain("email differs");
        DemoCustomerUiHelpers.ConflictSemantic(true).Should().Be(ManagementStatusSemantic.Warning);

        var clean = conflicted with { HasConflict = false, ConflictCount = 0, PrimaryConflictSummary = null };
        DemoCustomerUiHelpers.ConflictLabel(clean).Should().Be("No conflicts");
        DemoCustomerUiHelpers.ConflictSemantic(false).Should().Be(ManagementStatusSemantic.Success);
    }

    [Fact]
    public void Breadcrumbs_resolve_demo_customers_under_developer_tasks()
    {
        var list = ManagementBreadcrumbResolver.Resolve(DemoCustomerUiRoutes.List);
        list.Select(c => c.Label).Should().Equal("Config", "Developer & internal", "Demo Customers");
        list[^1].IsCurrent.Should().BeTrue();

        var detail = ManagementBreadcrumbResolver.Resolve(DemoCustomerUiRoutes.Detail("C-100"));
        detail.Select(c => c.Label).Should().Equal("Config", "Developer & internal", "Demo Customers", "C-100");
        detail.Should().Contain(c => c.Href == DemoCustomerUiRoutes.List);

        ManagementBreadcrumbResolver.Resolve("/demo/nested-nav").Should().BeEmpty();
    }

    private static IEnumerable<DomainMenuItem> Flatten(IEnumerable<DomainMenuItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item.Children is { Count: > 0 })
            {
                foreach (var child in Flatten(item.Children))
                    yield return child;
            }
        }
    }

    private static string RepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return Path.Combine(dir!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
