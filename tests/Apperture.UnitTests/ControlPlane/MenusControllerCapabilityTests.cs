using System.Reflection;
using Apperture.ControlPlane.Security.Models;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// <see cref="MenusController.HasCapability"/> ends in a permissive default arm, so a token that is defined
/// but never mapped renders for every operator. These tests make an omitted mapping fail loudly.
/// </summary>
public sealed class MenusControllerCapabilityTests
{
    private static OperatorCapabilitiesDto Caps(bool connectorDefinitions, bool notificationDefinitions) =>
        new(false, false, false, false, false,
            CanViewConnectorDefinitions: connectorDefinitions,
            CanViewNotificationDefinitions: notificationDefinitions);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ViewComponents_is_either_family_read_capability(bool connectorDefinitions, bool notificationDefinitions, bool expected)
    {
        MenusController.HasCapability(Caps(connectorDefinitions, notificationDefinitions), DomainMenuCapabilityTokens.ViewComponents)
            .Should().Be(expected);
    }

    [Fact]
    public void ViewComponents_does_not_fall_through_to_the_permissive_default()
    {
        // An operator with no capabilities at all must not see the Catalog leaf.
        MenusController.HasCapability(Caps(false, false), DomainMenuCapabilityTokens.ViewComponents).Should().BeFalse();
    }

    [Fact]
    public void Every_defined_menu_capability_token_is_explicitly_mapped()
    {
        var noCapabilities = Caps(false, false);
        var tokens = typeof(DomainMenuCapabilityTokens)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, FieldType: var t } && t == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        tokens.Should().NotBeEmpty();
        foreach (var token in tokens)
        {
            MenusController.HasCapability(noCapabilities, token)
                .Should().BeFalse("token '{0}' must be mapped to a capability, not fall through to the permissive default", token);
        }
    }

    [Fact]
    public void Unknown_token_keeps_the_documented_permissive_behaviour()
    {
        MenusController.HasCapability(Caps(false, false), "NotARealToken").Should().BeTrue();
    }
}
