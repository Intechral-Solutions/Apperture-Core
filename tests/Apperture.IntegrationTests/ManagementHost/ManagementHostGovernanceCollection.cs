using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

[CollectionDefinition(Name)]
public sealed class ManagementHostGovernanceCollection : ICollectionFixture<ManagementHostGovernanceFixture>
{
    public const string Name = "ManagementHostGovernance";
}
