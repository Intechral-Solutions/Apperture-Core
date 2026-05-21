using Apperture.Management.Ui.Shared;
using Apperture.Management.Ui.Shared.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic101ManagementBreadcrumbResolverTests
{
    [Theory]
    [InlineData("/dashboard", "Dashboard")]
    [InlineData("/control/center", "Operations", "Control Center")]
    [InlineData("/control/blocked-work", "Operations", "Blocked Work")]
    [InlineData("/control/audit", "Operations", "Audit")]
    [InlineData("/execute", "Operations", "Execute")]
    [InlineData("/runs", "Automation", "Runs")]
    [InlineData("/runs/run-1", "Automation", "Runs", "Run")]
    [InlineData("/schedules", "Automation", "Schedules")]
    [InlineData("/schedules/new", "Automation", "Schedules", "New")]
    [InlineData("/schedules/sch-1", "Automation", "Schedules", "Schedule")]
    [InlineData("/integrations", "Integrations")]
    [InlineData("/integrations/new", "Integrations", "New")]
    [InlineData("/integrations/int-1", "Integrations", "Integration")]
    [InlineData("/integrations/int-1/notifications", "Integrations", "Integration", "Notifications")]
    [InlineData("/integrations/int-1/schedule-diagnostics", "Integrations", "Integration", "Schedule diagnostics")]
    [InlineData("/connections", "Connections")]
    [InlineData("/connections/new", "Connections", "New")]
    [InlineData("/connections/conn-1", "Connections", "Connection")]
    [InlineData("/config", "Config", "Overview")]
    [InlineData("/config/auth", "Config", "Auth")]
    [InlineData("/config/governance", "Config", "Governance")]
    [InlineData("/config/notifications", "Config", "Notifications")]
    [InlineData("/config/notifications/definitions", "Config", "Notifications", "Definitions")]
    [InlineData("/config/notifications/definitions/new", "Config", "Notifications", "Definitions", "New")]
    [InlineData("/config/notifications/definitions/def-1", "Config", "Notifications", "Definitions", "Definition")]
    [InlineData("/config/notifications/definitions/def-1/edit", "Config", "Notifications", "Definitions", "Definition", "Edit")]
    [InlineData("/config/notifications/instances/inst-1", "Config", "Notifications", "Instances", "Instance")]
    [InlineData("/config/contacts", "Config", "Managed contacts")]
    [InlineData("/config/contacts/c-1", "Config", "Managed contacts", "Contact")]
    [InlineData("/modules", "Config", "Modules")]
    [InlineData("/modules/mod-1", "Config", "Modules", "Module")]
    public void Resolve_known_routes_returns_expected_labels(string path, params string[] expectedLabels)
    {
        var crumbs = ManagementBreadcrumbResolver.Resolve(path);
        crumbs.Select(c => c.Label).Should().Equal(expectedLabels);
    }

    [Theory]
    [InlineData("/runs/run-1", "/runs")]
    [InlineData("/integrations/int-1/notifications", "/integrations/int-1")]
    [InlineData("/config/notifications/definitions/def-1/edit", "/config/notifications/definitions/def-1")]
    [InlineData("/config/governance/users/u-1/role-derived-access", "/config/governance/users/u-1")]
    public void Resolve_parent_crumbs_link_to_stable_hubs(string path, string expectedParentHref)
    {
        var crumbs = ManagementBreadcrumbResolver.Resolve(path);
        crumbs.Should().HaveCountGreaterThan(1);
        var parent = crumbs[^2];
        parent.IsCurrent.Should().BeFalse();
        parent.Href.Should().Be(expectedParentHref);
    }

    [Fact]
    public void Resolve_current_crumb_has_no_href()
    {
        var crumbs = ManagementBreadcrumbResolver.Resolve("/runs/run-1");
        var current = crumbs[^1];
        current.Label.Should().Be("Run");
        current.IsCurrent.Should().BeTrue();
        current.Href.Should().BeNull();
    }

    [Theory]
    [InlineData("/login")]
    [InlineData("/login/local")]
    [InlineData("/demo/nested-nav")]
    [InlineData("/totally/unknown/route")]
    public void Resolve_excluded_or_unknown_paths_return_empty(string path)
    {
        ManagementBreadcrumbResolver.Resolve(path).Should().BeEmpty();
    }

    [Fact]
    public void Resolve_trailing_slash_matches_same_trail()
    {
        var a = ManagementBreadcrumbResolver.Resolve("/schedules/new");
        var b = ManagementBreadcrumbResolver.Resolve("/schedules/new/");
        a.Select(c => c.Label).Should().Equal(b.Select(c => c.Label));
    }

    [Fact]
    public void Resolve_governance_legacy_paths_use_config_canonical_hrefs()
    {
        var crumbs = ManagementBreadcrumbResolver.Resolve("/governance/users/u-1");
        crumbs.Select(c => c.Label).Should().Equal("Config", "Governance", "Users", "User");
        crumbs[1].Href.Should().Be("/config/governance");
        crumbs[2].Href.Should().Be("/config/governance/users");
    }

    [Fact]
    public void Resolve_attention_operational_teams_under_operations()
    {
        var crumbs = ManagementBreadcrumbResolver.Resolve("/attention/routing-defaults");
        crumbs.Select(c => c.Label).Should().Equal("Operations", "Routing defaults");
        crumbs[0].Href.Should().Be("/control/center");
    }

    [Theory]
    [InlineData("/runs/run-1")]
    [InlineData("/integrations/int-1/notifications")]
    [InlineData("/config/notifications/definitions/def-1/edit")]
    [InlineData("/schedules/sch-1")]
    [InlineData("/modules/mod-1")]
    public void Resolve_non_terminal_crumbs_always_have_hrefs(string path)
    {
        var crumbs = ManagementBreadcrumbResolver.Resolve(path);
        crumbs.Should().NotBeEmpty();
        foreach (var crumb in crumbs.Where(c => !c.IsCurrent))
        {
            crumb.Href.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Resolve_integration_child_entity_parent_uses_id_in_href()
    {
        var crumbs = ManagementBreadcrumbResolver.Resolve("/integrations/int-42/notifications");
        crumbs.Should().HaveCount(3);
        crumbs[1].Label.Should().Be("Integration");
        crumbs[1].Href.Should().Be("/integrations/int-42");
        crumbs[1].IsCurrent.Should().BeFalse();
    }

    [Fact]
    public void Resolve_definition_edit_entity_parent_links_to_definition_detail()
    {
        var crumbs = ManagementBreadcrumbResolver.Resolve("/config/notifications/definitions/def-9/edit");
        crumbs[^2].Label.Should().Be("Definition");
        crumbs[^2].Href.Should().Be("/config/notifications/definitions/def-9");
        crumbs[^1].IsCurrent.Should().BeTrue();
        crumbs[^1].Href.Should().BeNull();
    }
}
