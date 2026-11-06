using Apperture.BrowserTests.Infrastructure;
using Apperture.SqlTestTargets;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Apperture.BrowserTests;

/// <summary>
/// The harness drops and recreates its own database on every run, so the server it is pointed at is checked first. These cases
/// are not gated by <c>APPERTURE_BROWSER_TESTS</c> because none starts a host, a browser or reaches a real server: every address
/// is unroutable (203.0.113.0/24 is reserved for documentation) and every connection string carries Connect Timeout=1.
/// </summary>
public sealed class BrowserSqlTargetGuardTests
{
    private const string Secret = "S3cret!Pw-Marker";

    private static string Cs(string server, string catalog) =>
        $"Server={server};User Id=sa;Password={Secret};TrustServerCertificate=True;Connect Timeout=1;Initial Catalog={catalog};";

    [Fact]
    public void The_harness_database_replaces_the_base_catalog_on_a_disposable_test_server()
    {
        var scratch = BrowserSqlConfiguration.ScratchConnectionStringFor(Cs("127.0.0.1,1433", "Apperture"));

        var builder = new SqlConnectionStringBuilder(scratch);
        builder.InitialCatalog.Should().Be(BrowserSqlConfiguration.ScratchDatabaseName);
        builder.DataSource.Should().Be("127.0.0.1,1433");
    }

    [Fact]
    public void The_harness_database_name_is_itself_a_disposable_catalog()
    {
        var act = () => SqlTestTargetGuard.EnsureDisposableCatalog(BrowserSqlConfiguration.ScratchDatabaseName, "recreate the browser harness database");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("203.0.113.9,14333")]
    [InlineData("tcp:203.0.113.9,14333")]
    [InlineData("localhost\\SQLEXPRESS")]
    [InlineData("203.0.113.9,notaport")]
    public void A_base_connection_on_a_protected_or_unplaceable_server_is_refused(string server)
    {
        var act = () => BrowserSqlConfiguration.ScratchConnectionStringFor(Cs(server, "Apperture"));

        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().NotContain(Secret);
    }

    [Fact]
    public void An_unparseable_base_connection_is_refused()
    {
        var act = () => BrowserSqlConfiguration.ScratchConnectionStringFor("not a connection string");

        act.Should().Throw<SqlTestTargetRefusedException>();
    }

    [Fact]
    public async Task Recreating_the_harness_database_is_refused_on_the_development_sql_port_before_anything_connects()
    {
        var act = () => BrowserSqlConfiguration.RecreateScratchDatabaseAsync(Cs("203.0.113.9,14333", BrowserSqlConfiguration.ScratchDatabaseName));

        // Had the guard not run first, this would have failed as an InvalidOperationException from the unreachable connect.
        (await act.Should().ThrowAsync<SqlTestTargetRefusedException>()).Which.Message.Should().Contain("14333");
    }

    [Fact]
    public async Task Recreating_the_bare_development_catalog_is_refused_before_anything_connects()
    {
        var act = () => BrowserSqlConfiguration.RecreateScratchDatabaseAsync(Cs("203.0.113.9,1433", "Apperture"));

        (await act.Should().ThrowAsync<SqlTestTargetRefusedException>()).Which.Message.Should().Contain("database=Apperture]");
    }
}
