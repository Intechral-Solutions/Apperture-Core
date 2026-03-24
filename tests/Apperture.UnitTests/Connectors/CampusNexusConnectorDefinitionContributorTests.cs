using Apperture.Connectors.CampusNexus;
using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Connector;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Connectors;

public sealed class CampusNexusConnectorDefinitionContributorTests
{
    [Fact]
    public void Contribute_EnrichesCampusNexusDefinitionWithConnectionFields()
    {
        var registry = new StubConnectorDefinitionRegistry();
        registry.Register(WellKnownConnectorDefinitions.CreatePlatformCampusNexusMinimal());

        new CampusNexusConnectorDefinitionContributor().Contribute(registry);

        var def = registry.Get(WellKnownConnectorDefinitionIds.PlatformCampusNexus);
        def.Should().NotBeNull();
        def!.ConnectionFields.Should().NotBeEmpty();
        def.ConnectionFields.Should().Contain(f => f.Key == "BaseUrl" && !f.IsSecret);
        def.ConnectionFields.Should().Contain(f => f.Key == "ApiKey" && f.IsSecret);
    }
}
