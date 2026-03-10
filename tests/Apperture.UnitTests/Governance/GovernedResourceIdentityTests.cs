using Apperture.Core.Domain.Governance;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class GovernedResourceIdentityTests
{
    [Fact]
    public void GovernedResourceIdentity_creates_with_required_fields()
    {
        var identity = new GovernedResourceIdentity(
            "Connection",
            "conn-123",
            "module:foo",
            "owner-1");

        identity.ResourceType.Should().Be("Connection");
        identity.ResourceId.Should().Be("conn-123");
        identity.ScopeKey.Should().Be("module:foo");
        identity.OwnerId.Should().Be("owner-1");
    }

    [Fact]
    public void GovernedResourceIdentity_allows_null_scope_and_owner()
    {
        var identity = new GovernedResourceIdentity("Binding", "bind-456");

        identity.ScopeKey.Should().BeNull();
        identity.OwnerId.Should().BeNull();
    }
}
