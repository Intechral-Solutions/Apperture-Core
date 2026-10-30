using Apperture.SqlTestTargets;
using FluentAssertions;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// The disposable-target rules for test infrastructure. Pure: every case passes the opt-in explicitly, so nothing here reads the
/// environment, opens a connection or touches a database.
/// </summary>
public sealed class SqlTestTargetGuardTests
{
    private const string Secret = "S3cret!Pw-Marker";

    private static string Cs(string server, string catalog = "Apperture_Probe") =>
        $"Server={server};User Id=sa;Password={Secret};TrustServerCertificate=True;Initial Catalog={catalog};Connect Timeout=1;";

    private static void Disposable(string connectionString, bool governance = false, bool optIn = false) =>
        SqlTestTargetGuard.EnsureDisposableTarget(connectionString, "create a test catalog", governance, optIn);

    // ---- catalog rule -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Apperture")]
    [InlineData("apperture")]
    [InlineData("APPERTURE")]
    [InlineData(" Apperture")]
    [InlineData("Apperture ")]
    [InlineData("[Apperture]")]
    [InlineData("\"Apperture\"")]
    [InlineData("Apperture_")]
    [InlineData("master")]
    [InlineData("tempdb")]
    [InlineData("msdb")]
    [InlineData("ApperturePrime")]
    [InlineData("Apperture-Dev")]
    [InlineData("Apperture_x]; DROP DATABASE [master")]
    [InlineData("Apperture_x'")]
    [InlineData("Apperture_a b")]
    [InlineData("Apperture_ünï")]
    [InlineData("Apperture_Ａ")]
    [InlineData("Apperture_x\n")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_catalog_that_is_not_an_Apperture_underscore_name_is_refused(string catalog)
    {
        var act = () => SqlTestTargetGuard.EnsureDisposableCatalog(catalog, "drop a test catalog");

        act.Should().Throw<SqlTestTargetRefusedException>();
    }

    [Fact]
    public void The_bare_development_database_is_refused_with_a_message_that_names_it()
    {
        var act = () => Disposable(Cs("127.0.0.1,1433", "Apperture"));

        act.Should().Throw<SqlTestTargetRefusedException>()
            .Which.Message.Should().Contain("database=Apperture").And.Contain("never a disposable");
    }

    [Fact]
    public void A_catalog_name_over_the_sql_server_limit_is_refused()
    {
        var act = () => SqlTestTargetGuard.EnsureDisposableCatalog("Apperture_" + new string('a', 120), "create a test catalog");

        act.Should().Throw<SqlTestTargetRefusedException>();
    }

    [Theory]
    [InlineData("Apperture_IntegrationTests")]
    [InlineData("Apperture_Aud14Vfy0123456789ab")]
    [InlineData("Apperture_0")]
    [InlineData("apperture_lower_case")]
    public void A_disposable_catalog_name_is_accepted(string catalog)
    {
        var act = () => SqlTestTargetGuard.EnsureDisposableCatalog(catalog, "create a test catalog");

        act.Should().NotThrow();
    }

    // ---- governance suffix -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Apperture_ManagementGovernanceTests")]
    [InlineData("Apperture_BootstrapHttpTests")]
    [InlineData("Apperture_Tests")]
    [InlineData("Apperture_somethingtests")]
    public void A_governance_catalog_ending_in_Tests_is_accepted(string catalog)
    {
        var act = () => SqlTestTargetGuard.EnsureDisposableCatalog(catalog, "create the governance catalog", requireTestsSuffix: true);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Apperture_ManagementGovernance")]
    [InlineData("Apperture_Governance")]
    [InlineData("Apperture_TestsX")]
    [InlineData("Apperture")]
    [InlineData("master")]
    public void A_governance_catalog_without_the_Tests_suffix_is_refused(string catalog)
    {
        var act = () => SqlTestTargetGuard.EnsureDisposableCatalog(catalog, "create the governance catalog", requireTestsSuffix: true);

        act.Should().Throw<SqlTestTargetRefusedException>();
    }

    [Fact]
    public void The_Tests_suffix_is_only_required_when_asked_for()
    {
        var act = () => SqlTestTargetGuard.EnsureDisposableCatalog("Apperture_Scratch01", "create a test catalog");

        act.Should().NotThrow();
    }

    // ---- server rule: the development SQL port ------------------------------------------------------------------------

    [Theory]
    [InlineData("127.0.0.1,14333")]
    [InlineData("localhost,14333")]
    [InlineData("LOCALHOST,14333")]
    [InlineData("tcp:127.0.0.1,14333")]
    [InlineData("TCP:127.0.0.1,14333")]
    [InlineData("tcp:localhost,14333")]
    [InlineData(" 127.0.0.1 , 14333 ")]
    [InlineData("127.0.0.1,014333")]
    [InlineData("127.0.0.1,0014333")]
    [InlineData("[::1],14333")]
    [InlineData("tcp:[::1],14333")]
    [InlineData("host.docker.internal,14333")]
    [InlineData("203.0.113.7,14333")]
    [InlineData("some-other-host,14333")]
    [InlineData("admin:127.0.0.1,14333")]
    public void The_development_sql_port_is_refused_whatever_the_address_form(string server)
    {
        var act = () => Disposable(Cs(server));

        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().Contain("14333");
    }

    [Theory]
    [InlineData("Data Source=127.0.0.1,14333")]
    [InlineData("DATA SOURCE=127.0.0.1,14333")]
    [InlineData("Addr=127.0.0.1,14333")]
    [InlineData("Address=127.0.0.1,14333")]
    [InlineData("Network Address=127.0.0.1,14333")]
    [InlineData("SERVER=127.0.0.1,14333")]
    public void Every_keyword_alias_of_the_server_is_checked(string serverPair)
    {
        var connectionString = $"{serverPair};User Id=sa;Password={Secret};Initial Catalog=Apperture_Probe;";

        var act = () => Disposable(connectionString);

        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().Contain("14333");
    }

    [Fact]
    public void A_failover_partner_on_the_development_sql_port_is_refused()
    {
        var connectionString = Cs("127.0.0.1,1433") + "Failover Partner=127.0.0.1,14333;";

        var act = () => Disposable(connectionString);

        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().Contain("failover partner");
    }

    [Fact]
    public void The_explicit_opt_in_lets_the_development_sql_port_through_the_guard()
    {
        var act = () => Disposable(Cs("127.0.0.1,14333"), optIn: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void The_opt_in_never_relaxes_the_catalog_rule()
    {
        var act = () => Disposable(Cs("127.0.0.1,14333", "Apperture"), optIn: true);

        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().Contain("never a disposable");
    }

    // ---- server rule: unrecognised or ambiguous targets ---------------------------------------------------------------

    [Theory]
    [InlineData("127.0.0.1,abc")]
    [InlineData("127.0.0.1,")]
    [InlineData("127.0.0.1,0")]
    [InlineData("127.0.0.1,70000")]
    [InlineData("127.0.0.1,-1433")]
    [InlineData("127.0.0.1,1433.0")]
    [InlineData("127.0.0.1,１４３３３")]
    [InlineData("127.0.0.1,14333,1433")]
    [InlineData("[::1")]
    [InlineData("[::1]x")]
    [InlineData(",1433")]
    [InlineData("np:\\\\.\\pipe\\sql\\query")]
    [InlineData("lpc:localhost")]
    [InlineData("via:localhost,1433")]
    [InlineData(".\\SQLEXPRESS")]
    [InlineData("localhost\\SQLEXPRESS")]
    [InlineData("tcp:db-host\\prod")]
    public void An_unrecognised_or_ambiguous_server_is_refused_without_the_opt_in(string server)
    {
        var act = () => Disposable(Cs(server));

        act.Should().Throw<SqlTestTargetRefusedException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_server_is_refused(string server)
    {
        var act = () => Disposable($"Server={server};User Id=sa;Password={Secret};Initial Catalog=Apperture_Probe;");

        act.Should().Throw<SqlTestTargetRefusedException>();
    }

    [Theory]
    [InlineData("this is not a connection string")]
    [InlineData("Server=127.0.0.1,1433;Port=14333;Initial Catalog=Apperture_Probe")]
    public void An_unparseable_connection_string_is_refused_and_not_echoed(string connectionString)
    {
        var act = () => Disposable(connectionString);

        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().Contain("unparseable");
    }

    [Fact]
    public void A_named_instance_is_accepted_only_with_the_explicit_opt_in()
    {
        var act = () => Disposable(Cs("localhost\\SQLEXPRESS"), optIn: true);

        act.Should().NotThrow();
    }

    // ---- server rule: accepted forms ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("127.0.0.1,1433")]
    [InlineData("localhost,1433")]
    [InlineData("tcp:127.0.0.1,1433")]
    [InlineData("127.0.0.1,58361")]
    [InlineData("127.0.0.1,14334")]
    [InlineData("127.0.0.1,1434")]
    [InlineData("ci,1433")]
    [InlineData("ci")]
    [InlineData("[::1],1433")]
    [InlineData("host.docker.internal,1433")]
    [InlineData("(localdb)\\mssqllocaldb")]
    [InlineData("(LocalDB)\\MSSQLLocalDB")]
    public void A_disposable_test_server_is_accepted(string server)
    {
        var act = () => Disposable(Cs(server));

        act.Should().NotThrow();
    }

    // ---- diagnostics ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_refusal_names_the_target_and_the_catalog_but_never_a_credential()
    {
        var act = () => Disposable(Cs("203.0.113.7,14333", "Apperture_Probe"));

        var message = act.Should().Throw<SqlTestTargetRefusedException>().Which.Message;

        message.Should().Contain("203.0.113.7,14333").And.Contain("Apperture_Probe")
            .And.Contain(SqlTestTargetGuard.ProtectedTargetOptInVariable);
        message.Should().NotContain(Secret).And.NotContain("Password").And.NotContain("sa;");
    }

    [Fact]
    public void An_unparseable_target_message_never_echoes_the_input()
    {
        var act = () => Disposable("Server=x;Password=" + Secret + ";Port=1");

        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().NotContain(Secret);
    }

    [Fact]
    public void A_refusal_is_not_an_InvalidOperationException_so_a_soft_skip_cannot_swallow_it()
    {
        var thrown = Record.Exception(() => Disposable(Cs("127.0.0.1,14333")));

        thrown.Should().BeOfType<SqlTestTargetRefusedException>();
        thrown.Should().NotBeAssignableTo<InvalidOperationException>();
    }

    [Fact]
    public void Describe_lists_only_the_data_source_and_the_catalog()
    {
        var description = SqlTestTargetGuard.Describe(Cs("127.0.0.1,1433", "Apperture_Probe"));

        description.Should().Contain("127.0.0.1,1433").And.Contain("Apperture_Probe");
        description.Should().NotContain(Secret).And.NotContain("User Id").And.NotContain("Password");
    }

    // ---- drop of a named catalog on a base server -----------------------------------------------------------------------

    [Fact]
    public void Dropping_a_catalog_is_checked_against_the_catalog_name_and_not_the_base_connection_catalog()
    {
        // The base connection string legitimately names the development catalog as its default (CI does); only the catalog
        // actually being created or dropped matters.
        var baseConnection = Cs("127.0.0.1,1433", "Apperture");

        var ok = () => SqlTestTargetGuard.EnsureDisposableCatalogOnServer(baseConnection, "Apperture_Scratch01", "drop a test catalog");
        var bad = () => SqlTestTargetGuard.EnsureDisposableCatalogOnServer(baseConnection, "Apperture", "drop a test catalog");

        ok.Should().NotThrow();
        bad.Should().Throw<SqlTestTargetRefusedException>();
    }

    [Fact]
    public void Dropping_on_the_development_sql_port_is_refused_even_for_a_disposable_name()
    {
        var act = () => SqlTestTargetGuard.EnsureDisposableCatalogOnServer(
            Cs("127.0.0.1,14333", "Apperture"), "Apperture_Scratch01", "drop a test catalog", protectedTargetOptIn: false);

        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().Contain("14333");
    }
}
