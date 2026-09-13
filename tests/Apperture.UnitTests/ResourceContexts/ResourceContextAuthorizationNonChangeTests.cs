using System.Reflection;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// ResourceContextId is ownership / namespace. ScopeKey is authorization. CTX-1 adds the first and
/// changes nothing about the second.
/// </summary>
public sealed class ResourceContextAuthorizationNonChangeTests
{
    /// <summary>
    /// A <c>context:{id}</c> scope-key form is a later epic's concern. Introducing one here would
    /// silently start narrowing grants.
    /// </summary>
    [Fact]
    public void NoContextScopeKeyFormExists()
    {
        var members = typeof(ScopeKeyConstants)
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToList();

        members.Should().NotContain(n => n.Contains("Context", StringComparison.OrdinalIgnoreCase));
        members.Should().Contain(["Instance", "Environment", "Module", "Connection", "Operation", "RuntimeInstance", "Slot"]);
    }

    /// <summary>Permissions on ResourceContexts themselves are CTX-3-or-later.</summary>
    [Fact]
    public void NoResourceContextResourceTypeExists()
    {
        var names = typeof(ResourceTypeConstants)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        names.Should().NotContain("ResourceContext");
        names.Should().Contain(["Integration", "Connection", "Binding"]);
    }

    /// <summary>
    /// Instance still contains every scope, so existing instance-level grants keep covering every
    /// resource exactly as before ownership existed.
    /// </summary>
    [Fact]
    public void InstanceScopeStillContainsEverything()
    {
        ScopeKeyConstants.Instance.Should().Be("instance");
        ScopeKeyConstants.Connection("c-1").Should().Be("connection:c-1");
        ScopeKeyConstants.Slot("Sample.Mod", "Db").Should().Be("module:Sample.Mod:slot:Db");
    }

    /// <summary>
    /// The governed identity an Integration reports is derived from ScopeKey alone. Ownership must not
    /// leak into it.
    /// </summary>
    [Fact]
    public async Task GovernedIdentities_IgnoreResourceContextOwnership()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var app = await h.ResourceContexts.CreateApplicationContextAsync("Application A");

        var integration = await h.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Payments", "Sample.Mod", ResourceContextId: app.Id));
        var connection = await h.Connections.CreateConnectionAsync(new CreateConnectionRequest(
            new ConnectorDefinitionId("test.connector"),
            "Ops",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            ResourceContextId: app.Id));

        var integrationIdentity = integration.GetIdentity();
        integrationIdentity.ScopeKey.Should().Be(ScopeKeyConstants.Instance, "no ScopeKey was supplied, so it defaults as before");
        integrationIdentity.ResourceType.Should().Be(ResourceTypeConstants.Integration);

        var connectionIdentity = connection.GetIdentity();
        connectionIdentity.ScopeKey.Should().Be(ScopeKeyConstants.Instance);
        connectionIdentity.ResourceType.Should().Be(ResourceTypeConstants.Connection);

        // Ownership is recorded, and separately.
        integration.ResourceContextId.Value.Should().Be(app.Id.Value);
        connection.ResourceContextId.Value.Should().Be(app.Id.Value);
    }

    /// <summary>Backfilled and default-owned resources sit in Shared without any authorization change.</summary>
    [Fact]
    public async Task DefaultOwnershipDoesNotAlterVisibility()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("One", "Sample.Mod"));
        await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Two", "Sample.Mod", ScopeKey: "environment:prod"));

        var visible = await h.IntegrationRepository.GetVisibleAsync(ScopeKeyConstants.Instance);

        visible.Should().HaveCount(2, "instance scope contains every scope, exactly as before");
        visible.Should().OnlyContain(i => i.ResourceContextId.Value == ResourceContextIds.SharedValue);
    }
}
