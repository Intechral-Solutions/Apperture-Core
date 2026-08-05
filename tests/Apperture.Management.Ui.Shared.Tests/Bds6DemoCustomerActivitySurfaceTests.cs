using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Bds6DemoCustomerActivitySurfaceTests
{
    [Fact]
    public void Activity_route_and_helpers_are_wired()
    {
        DemoCustomerUiRoutes.Activity("C-100").Should().Be("/demo/customers/C-100/activity");
        DemoCustomerUiRoutes.Activity("a b").Should().Contain("activity");

        var filter = DemoCustomerActivityUiHelpers.BuildFilter(
            "file_import",
            "Failed",
            attentionOnly: true,
            query: "import",
            lookbackDays: "30",
            skip: 25);
        filter.SourceKind.Should().Be("file_import");
        filter.Status.Should().Be("Failed");
        filter.AttentionOnly.Should().BeTrue();
        filter.LookbackDays.Should().Be(30);
        filter.Skip.Should().Be(25);

        DemoCustomerActivityUiHelpers.ResolveEmptyCopy(filtersActive: false).Title
            .Should().Be(DemoCustomerActivityUiHelpers.EmptyTitle);
        DemoCustomerActivityUiHelpers.ResolveEmptyCopy(filtersActive: true).Title
            .Should().Be(DemoCustomerActivityUiHelpers.EmptyFilterTitle);

        DemoCustomerActivityUiHelpers.SectionTitle.Should().NotContain("UIX");
        DemoCustomerActivityUiHelpers.SectionTitle.Should().NotContain("BDS");
        DemoCustomerActivityUiHelpers.PageSubtitle.Should().NotContain("Operation Item Explorer");
    }

    [Fact]
    public void Activity_links_reuse_typed_return_context()
    {
        var entry = new DemoCustomerActivityEntryDto(
            BusinessKey: "C-100",
            OccurredAtUtc: DateTimeOffset.UtcNow,
            RunId: "run-1",
            RunStatus: "Succeeded",
            IntegrationId: "int-1",
            IntegrationLabel: "Import",
            SourceKind: "file_import",
            SourceKindLabel: "File import",
            OperationGroupId: "g1",
            OperationItemId: "i1",
            OperationKind: "file_import",
            OperationLabel: "imported",
            SubjectKey: "C-100",
            DisplayLabel: "Valid One",
            ItemStatus: "Succeeded",
            ChangeKind: "imported",
            ErrorCode: null,
            ErrorMessage: null,
            IsRetryable: false,
            RetryCount: 0,
            RequiresAttention: false,
            WasRetried: false,
            SourceLabel: null,
            GroupStatus: "Succeeded");

        var href = DemoCustomerActivityUiHelpers.BuildAffectedHref("C-100", entry);
        href.Should().NotBeNull();
        href.Should().Contain("/runs/run-1");
        href.Should().Contain("group=g1");
        href.Should().Contain("item=i1");
        href.Should().Contain("from=demo-customer");
        href.Should().Contain("fromKey=C-100");
        href.Should().NotContain("from=%2F");

        DemoCustomerActivityUiHelpers.ResolveActionLabel(entry).Should().Be("Open affected record");

        var groupOnly = entry with { OperationItemId = "" };
        // Empty item id still passed — BuildDemoCustomerSourceHref treats whitespace item as omitted
        var groupHref = AffectedRecordNavigation.BuildDemoCustomerSourceHref(
            "C-100", "run-1", "g1", null);
        groupHref.Should().Contain("group=g1");
        groupHref.Should().NotContain("item=");
        AffectedRecordNavigation.ResolveDemoCustomerSourceLinkLabel("run-1", "g1", null)
            .Should().Be("Open affected group");

        DemoCustomerActivityUiHelpers.BuildAffectedHref("C-100", entry with { RunId = "" })
            .Should().BeNull();
    }

    [Fact]
    public void Breadcrumb_activity_route_is_route_derived()
    {
        var crumbs = ManagementBreadcrumbResolver.Resolve("/demo/customers/C-100/activity");
        crumbs.Select(c => c.Label).Should().Equal(
            "Config",
            "Developer & internal",
            "Demo Customers",
            "C-100",
            "Activity");
        crumbs[^2].Href.Should().Be(DemoCustomerUiRoutes.Detail("C-100"));
        crumbs[^1].IsCurrent.Should().BeTrue();
    }

    [Fact]
    public void Activity_page_and_detail_summary_exist_without_planning_labels()
    {
        var activity = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client",
            "Pages", "Demo", "DemoCustomerActivity.razor"));
        activity.Should().Contain("@page \"/demo/customers/{BusinessKey}/activity\"");
        activity.Should().Contain("GetDemoCustomerActivityAsync");
        activity.Should().Contain("Needs attention");
        activity.Should().Contain("BuildAffectedHref");
        activity.Should().Contain("Load more");
        activity.Should().NotContain("Operation Item Explorer");
        activity.Should().NotContain("BDS-6");
        activity.Should().NotContain("UIX-");

        var detail = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client",
            "Pages", "Demo", "DemoCustomerDetail.razor"));
        detail.Should().Contain("GetDemoCustomerActivitySummaryAsync");
        detail.Should().Contain("View activity");
        detail.Should().Contain("DemoCustomerUiRoutes.Activity");
    }

    [Fact]
    public void Link_integrity_activity_route_and_context_clearing()
    {
        AffectedRecordNavigation.WithoutContext(
                "/demo/customers/C-100/activity?from=demo-customer&fromKey=C-100")
            .Should().Be("/demo/customers/C-100/activity");

        ManagementNavigationContextCatalog.SupportedTypes
            .Should().Contain(ManagementNavigationReturnContext.DemoCustomerType);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Apperture.slnx"))
                || File.Exists(Path.Combine(dir.FullName, "Apperture.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
