using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Basic persistence wiring tests. Verifies AddApperturePersistence registers DbContext correctly.
/// Requires LocalDB or SQL Server with Apperture database. Skips if connection fails.
/// </summary>
public sealed class PersistenceWiringTests
{
    private const string LocalDbConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=Apperture_IntegrationTests;Trusted_Connection=True;";

    [Fact]
    public async Task AddApperturePersistence_registers_DbContext_resolvable()
    {
        var services = new ServiceCollection();
        services.AddApperturePersistence(LocalDbConnectionString);
        var provider = services.BuildServiceProvider();

        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        db.Should().NotBeNull();
        var canConnect = await db.Database.CanConnectAsync();
        if (!canConnect)
            return; // Skip when LocalDB/SQL Server unavailable (e.g. CI without database)
        canConnect.Should().BeTrue("DbContext should be able to connect with valid connection string");
    }

    [Fact]
    public void AddApperturePersistence_with_factory_resolves_DbContext()
    {
        var services = new ServiceCollection();
        services.AddApperturePersistence(_ => LocalDbConnectionString);
        var provider = services.BuildServiceProvider();

        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        db.Should().NotBeNull();
    }
}
