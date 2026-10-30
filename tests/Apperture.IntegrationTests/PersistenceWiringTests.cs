using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Basic persistence wiring tests. Verifies AddApperturePersistence registers DbContext correctly.
/// The wiring assertions need no database. The connectivity assertion runs against the configured test SQL Server only (never an
/// implicit LocalDB): without one it returns early in optional mode and fails when APPERTURE_REQUIRE_SQL_TESTS=1
/// (see <see cref="SqlTestExecution"/>).
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class PersistenceWiringTests
{
    [Fact]
    public async Task AddApperturePersistence_registers_DbContext_resolvable()
    {
        var wiring = new ServiceCollection();
        wiring.AddApperturePersistence(SqlTestExecution.NonConnectingConnectionString);
        using (var wiringProvider = wiring.BuildServiceProvider())
        using (var wiringScope = wiringProvider.CreateScope())
            wiringScope.ServiceProvider.GetRequiredService<AppertureDbContext>().Should().NotBeNull();

        var connectionString = SqlTestExecution.ConnectionStringOrNotExecuted();
        if (connectionString is null)
            return;

        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);
        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        db.Should().NotBeNull();
        var canConnect = await db.Database.CanConnectAsync();
        canConnect.Should().BeTrue("DbContext should be able to connect with valid connection string");
    }

    [Fact]
    public void AddApperturePersistence_with_factory_resolves_DbContext()
    {
        var services = new ServiceCollection();
        services.AddApperturePersistence(_ => SqlTestExecution.NonConnectingConnectionString);
        var provider = services.BuildServiceProvider();

        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        db.Should().NotBeNull();
    }
}
