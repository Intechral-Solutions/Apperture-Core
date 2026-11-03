using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// The catalog of the tests that depend on the first-administrator state: the bootstrap tests, which have to start from "no users at
/// all" and so delete every user of the catalog they run in, and the account tests, which log in as the first local user and need it
/// to be the administrator the bootstrap created. In the shared bootstrap catalog that reset would also remove the users the other
/// classes of that collection rely on, and the account tests would find those classes' users instead. Here only these two classes
/// live, and each starts from whatever state it needs (empty, or one administrator with a known password) in any order.
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
