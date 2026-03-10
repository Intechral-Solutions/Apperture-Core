using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Connector;

public sealed class BindingTests
{
    [Fact]
    public void Binding_implements_IGovernedResource()
    {
        var binding = new Binding(
            new BindingId("bind-1"),
            "ModuleA",
            "DbConnection",
            "System.Data.IDbConnection",
            new ConnectionId("conn-1"),
            null,
            BindingState.Active);

        var identity = binding.GetIdentity();

        identity.ResourceType.Should().Be(ResourceTypeConstants.Binding);
        identity.ResourceId.Should().Be("bind-1");
        identity.ScopeKey.Should().Be("module:ModuleA:slot:DbConnection");
    }

    [Fact]
    public void Binding_SlotScopeKey_formats_correctly()
    {
        var binding = new Binding(
            new BindingId("b1"),
            "MyModule",
            "ApiClient",
            "HttpClient",
            null,
            "cap-xyz",
            BindingState.Active);

        binding.SlotScopeKey.Should().Be("module:MyModule:slot:ApiClient");
    }
}
