using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BootstrapHttpCollection : ICollectionFixture<BootstrapHttpFixture>
{
    public const string Name = "BootstrapHttp";
}
