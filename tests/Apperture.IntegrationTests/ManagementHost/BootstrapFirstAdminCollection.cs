using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// The catalog of the first-administrator bootstrap tests alone. Those tests have to start from "no users at all", which means
/// deleting every user of the catalog they run in; in the shared bootstrap catalog that would also remove the users the other
/// classes of that collection rely on. Here nothing else lives in the catalog, so the reset touches only these tests.
/// </summary>
public sealed class BootstrapFirstAdminFixture : BootstrapHttpFixture
{
    protected override string DatabaseName => "Apperture_BootstrapFirstAdminTests";
}

/// <summary>Serial, like the shared bootstrap collection: it does not run beside another collection, nor its tests beside each other.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BootstrapFirstAdminCollection : ICollectionFixture<BootstrapFirstAdminFixture>
{
    public const string Name = "BootstrapFirstAdmin";
}
