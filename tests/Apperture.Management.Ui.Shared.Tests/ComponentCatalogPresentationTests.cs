using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class ComponentCatalogPresentationTests
{
    [Theory]
    [InlineData("connector", "Connector")]
    [InlineData("notification-definition", "Notification definition")]
    [InlineData("something-new", "something-new")]
    public void KindLabel_is_readable_and_falls_back_to_the_token(string kind, string expected) =>
        ComponentCatalogPresentation.KindLabel(kind).Should().Be(expected);

    [Theory]
    [InlineData("system", "System")]
    [InlineData("module", "Module")]
    [InlineData("other", "other")]
    public void OriginLabel_is_readable_and_falls_back_to_the_token(string origin, string expected) =>
        ComponentCatalogPresentation.OriginLabel(origin).Should().Be(expected);

    [Fact]
    public void SuppliedBy_is_the_platform_for_system_components_and_the_module_id_otherwise()
    {
        ComponentCatalogPresentation.SuppliedBy(null).Should().Be("Platform");
        ComponentCatalogPresentation.SuppliedBy("Sample.HelloWorld").Should().Be("Sample.HelloWorld");
        ComponentCatalogPresentation.SuppliedBy("  ").Should().Be("Platform");
    }

    [Theory]
    [InlineData("connector", "Apperture.Platform.Sql", "/components/connector/Apperture.Platform.Sql")]
    [InlineData("notification-definition", "platform.schedule.expected_run_missing", "/components/notification-definition/platform.schedule.expected_run_missing")]
    [InlineData("connector", "odd id/with slash", "/components/connector/odd%20id%2Fwith%20slash")]
    public void DetailHref_escapes_each_segment_and_keeps_dotted_native_ids(string kind, string nativeId, string expected) =>
        ComponentCatalogPresentation.DetailHref(kind, nativeId).Should().Be(expected);

    [Fact]
    public void CreateConnectionHref_preselects_the_connector_definition()
    {
        ComponentCatalogPresentation.CreateConnectionHref("Apperture.Platform.Sql")
            .Should().Be("/connections/new?connectorDefinitionId=Apperture.Platform.Sql");
        ComponentCatalogPresentation.CreateConnectionHref("a b")
            .Should().Be("/connections/new?connectorDefinitionId=a%20b");
    }

    [Fact]
    public void NotificationDefinitionHref_targets_the_authoritative_specialized_page()
    {
        ComponentCatalogPresentation.NotificationDefinitionHref("platform.schedule.expected_run_missing")
            .Should().Be("/config/notifications/definitions/platform.schedule.expected_run_missing");
    }

    [Theory]
    [InlineData("connector", true, "connector")]
    [InlineData("workflow", true, null)]
    [InlineData("", true, null)]
    [InlineData(null, true, null)]
    [InlineData("connector", false, null)]
    public void NormalizeKind_keeps_only_an_offered_kind(string? requested, bool offered, string? expected)
    {
        IReadOnlyList<string> available = offered ? ["connector"] : [];

        ComponentCatalogPresentation.NormalizeKind(requested, available).Should().Be(expected);
    }

    [Theory]
    [InlineData("system", "system")]
    [InlineData("module", "module")]
    [InlineData("user", null)]
    [InlineData("System", null)]
    [InlineData(null, null)]
    public void NormalizeOrigin_accepts_only_the_two_catalog_origins(string? requested, string? expected) =>
        ComponentCatalogPresentation.NormalizeOrigin(requested).Should().Be(expected);

    [Fact]
    public void NormalizeModule_keeps_only_an_offered_module()
    {
        IReadOnlyList<ComponentModuleRefDto> available = [new("Sample.HelloWorld")];

        ComponentCatalogPresentation.NormalizeModule("Sample.HelloWorld", available).Should().Be("Sample.HelloWorld");
        ComponentCatalogPresentation.NormalizeModule("Gone.Module", available).Should().BeNull();
        ComponentCatalogPresentation.NormalizeModule(null, available).Should().BeNull();
    }

    [Theory]
    [InlineData(0, 0, "")]
    [InlineData(1, 1, "1 component")]
    [InlineData(7, 7, "7 components")]
    [InlineData(3, 7, "Showing 3 of 7 components")]
    public void ResultsSummary_reports_shown_and_matching_counts(int shown, int total, string expected) =>
        ComponentCatalogPresentation.ResultsSummary(shown, total).Should().Be(expected);

    [Fact]
    public void IsFiltered_is_true_when_any_control_narrows_the_list()
    {
        ComponentCatalogPresentation.IsFiltered(null, null, null, "").Should().BeFalse();
        ComponentCatalogPresentation.IsFiltered(null, null, null, "  ").Should().BeFalse();
        ComponentCatalogPresentation.IsFiltered("connector", null, null, "").Should().BeTrue();
        ComponentCatalogPresentation.IsFiltered(null, "system", null, "").Should().BeTrue();
        ComponentCatalogPresentation.IsFiltered(null, null, "M", "").Should().BeTrue();
        ComponentCatalogPresentation.IsFiltered(null, null, null, "sql").Should().BeTrue();
    }

    [Fact]
    public void ConnectorDetailHref_targets_the_connector_component_detail() =>
        ComponentCatalogPresentation.ConnectorDetailHref("Apperture.Platform.Sql")
            .Should().Be("/components/connector/Apperture.Platform.Sql");

    [Theory]
    [InlineData("c1", "/connections/c1")]
    [InlineData("a b", "/connections/a%20b")]
    public void ConnectionHref_links_to_the_existing_connection_page(string id, string expected) =>
        ComponentCatalogPresentation.ConnectionHref(id).Should().Be(expected);

    [Theory]
    [InlineData(0, 0, "No active connections use this connector yet.")]
    [InlineData(1, 1, "1 active connection uses this connector.")]
    [InlineData(4, 4, "4 active connections use this connector.")]
    [InlineData(30, 25, "30 active connections use this connector. Showing the first 25.")]
    public void ConnectorUsageSummary_states_the_count_and_any_truncation(int count, int shown, string expected) =>
        ComponentCatalogPresentation.ConnectorUsageSummary(count, shown).Should().Be(expected);

    [Theory]
    [InlineData(0, "No current subscriptions.")]
    [InlineData(1, "1 current subscription.")]
    [InlineData(6, "6 current subscriptions.")]
    public void SubscriptionUsageSummary_states_the_current_subscription_count(int count, string expected) =>
        ComponentCatalogPresentation.SubscriptionUsageSummary(count).Should().Be(expected);

    [Fact]
    public void Usage_copy_carries_no_planning_labels_and_never_mentions_notification_instances()
    {
        var copy = new[]
        {
            ComponentCatalogPresentation.ConnectorUsageSummary(0, 0),
            ComponentCatalogPresentation.ConnectorUsageSummary(30, 25),
            ComponentCatalogPresentation.SubscriptionUsageSummary(0),
            ComponentCatalogPresentation.SubscriptionUsageSummary(3),
        };

        copy.Should().NotContain(c => c.Contains("CMP", StringComparison.Ordinal));
        copy.Should().NotContain(c => c.Contains("instance", StringComparison.OrdinalIgnoreCase),
            "historical notification instances are not usage");
    }

    [Fact]
    public void Copy_carries_no_planning_labels()
    {
        var copy = new[]
        {
            ComponentCatalogPresentation.KindLabel("connector"),
            ComponentCatalogPresentation.KindLabel("notification-definition"),
            ComponentCatalogPresentation.OriginLabel("system"),
            ComponentCatalogPresentation.OriginLabel("module"),
            ComponentCatalogPresentation.EmptyMessage,
            ComponentCatalogPresentation.NoMatchMessage,
        };

        copy.Should().NotContain(c => c.Contains("CMP", StringComparison.Ordinal) || c.Contains("Epic", StringComparison.OrdinalIgnoreCase));
    }
}
