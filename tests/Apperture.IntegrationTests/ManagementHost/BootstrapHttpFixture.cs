using Apperture.Persistence.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// Isolated SQL database for bootstrap HTTP tests (no governance user seed).
/// </summary>
public sealed class BootstrapHttpFixture : IAsyncLifetime
{
    private const string DatabaseName = "Apperture_BootstrapHttpTests";

    public string? ConnectionString { get; private set; }

    public ManagementHostWebApplicationFactory? Factory { get; private set; }

    public async Task InitializeAsync()
    {
        // No implicit fallback (such as a local LocalDB): without a reachable configured target the fixture fails, and so does every
        // test of the collection, with the configuration message, whatever APPERTURE_REQUIRE_SQL_TESTS says.
        var baseCs = SqlServerTestConfiguration.GetGovernanceConnectionString()
            ?? throw new InvalidOperationException(
                "These tests require a reachable test SQL Server. " + SqlServerTestConfiguration.DescribeActiveConfiguration());
        var builder = new SqlConnectionStringBuilder(baseCs) { InitialCatalog = DatabaseName };
        ConnectionString = builder.ConnectionString;

        SqlServerTestConfiguration.EnsureTestDatabaseExists(ConnectionString);

        Factory = new ManagementHostWebApplicationFactory(ConnectionString);
        using var client = Factory.CreateClient();
        _ = await client.GetAsync("/health").ConfigureAwait(false);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        await AppertureDatabaseMigrator.MigrateAsync(db).ConfigureAwait(false);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
