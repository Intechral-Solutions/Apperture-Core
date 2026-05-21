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
    [InlineData("/config/contacts", "Config", "Attention", "Managed contacts")]
    [InlineData("/config/contacts/c-1", "Config", "Attention", "Managed contacts", "Contact")]
    [InlineData("/attention/operational-teams", "Config", "Attention", "Operational teams")]
    [InlineData("/attention/routing-defaults", "Config", "Attention", "Routing defaults")]
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
    public void ResolveOrientation_terminal_title_not_in_parent_trail()
    {
        var o = ManagementBreadcrumbResolver.ResolveOrientation("/runs/run-1");
        o.Title.Should().Be("Run");
        o.Parents.Select(c => c.Label).Should().Equal("Automation", "Runs");
        o.Parents.Should().OnlyContain(c => !c.IsCurrent && !string.IsNullOrWhiteSpace(c.Href));
        ManagementBreadcrumbResolver.Resolve("/runs/run-1").Select(c => c.Label)
            .Should().Equal("Automation", "Runs", "Run");
    }

    [Fact]
    public void ResolveOrientation_single_segment_has_no_parents()
    {
        var o = ManagementBreadcrumbResolver.ResolveOrientation("/integrations");
        o.Title.Should().Be("Integrations");
        o.Parents.Should().BeEmpty();
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
    public void Resolve_attention_routing_defaults_under_config_attention()
    {
        var crumbs = ManagementBreadcrumbResolver.Resolve("/attention/routing-defaults");
        crumbs.Select(c => c.Label).Should().Equal("Config", "Attention", "Routing defaults");
        crumbs[0].Href.Should().Be(ShellSectionRoutes.ConfigHome);
        crumbs[1].Href.Should().Be("/attention/operational-teams");
    }

    [Theory]
    [InlineData("/runs/run-1")]
    [InlineData("/integrations/int-1/notifications")]
    [InlineData("/config/notifications/definitions/def-1/edit")]
    [InlineData("/schedules/sch-1")]
    [InlineData("/modules/mod-1")]
    public void ResolveOrientation_parent_crumbs_always_have_hrefs(string path)
    {
        var o = ManagementBreadcrumbResolver.ResolveOrientation(path);
        o.Title.Should().NotBeNullOrWhiteSpace();
        foreach (var crumb in o.Parents)
        {
            crumb.Href.Should().NotBeNullOrWhiteSpace();
            crumb.IsCurrent.Should().BeFalse();
        }
    }

    [Fact]
    public void Resolve_integration_child_entity_parent_uses_id_in_href()
    {
        var o = ManagementBreadcrumbResolver.ResolveOrientation("/integrations/int-42/notifications");
        o.Title.Should().Be("Notifications");
        o.Parents.Should().HaveCount(2);
        o.Parents[1].Label.Should().Be("Integration");
        o.Parents[1].Href.Should().Be("/integrations/int-42");
    }

    [Fact]
    public void Resolve_definition_edit_entity_parent_links_to_definition_detail()
    {
        var o = ManagementBreadcrumbResolver.ResolveOrientation("/config/notifications/definitions/def-9/edit");
        o.Title.Should().Be("Edit");
        o.Parents[^1].Label.Should().Be("Definition");
        o.Parents[^1].Href.Should().Be("/config/notifications/definitions/def-9");
    }

    [Fact]
    public void ResolveOrientation_hub_routes_may_include_subtitle()
    {
        ManagementBreadcrumbResolver.ResolveOrientation("/control/center").Subtitle
            .Should().NotBeNullOrWhiteSpace();
    }
}
