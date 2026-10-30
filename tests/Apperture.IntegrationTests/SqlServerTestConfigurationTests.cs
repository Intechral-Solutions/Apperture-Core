using Apperture.SqlTestTargets;
using FluentAssertions;
using Xunit;

namespace Apperture.IntegrationTests;

/// <remarks>
/// These tests clear and set process-wide environment variables (the SQL connection strings) and flip the configuration's global
/// switches. Left in the implicit parallel collection of their own they ran alongside the Management Host fixtures, which read
/// <c>APPERTURE_TEST_SQL</c> when they start, so a fixture that started in the same moment saw no connection string, fell back to
/// LocalDB and failed every test of its collection (observed in CI on SEC-2.3). The serialized SQL collection runs them alone.
/// </remarks>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class SqlServerTestConfigurationTests
{
    private readonly Dictionary<string, string?> _saved = new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void GetConnectionString_prefers_explicit_connection_string()
    {
        using var _ = new EnvScope(_saved);
        Set(Default: null, Password: "legacy-pass", ExplicitCs: "Server=127.0.0.1,58361;User Id=sa;Password=aspire;TrustServerCertificate=True;Initial Catalog=Apperture;");

        var cs = SqlServerTestConfiguration.GetConnectionString();

        cs.Should().NotBeNull();
        cs.Should().Contain("58361");
        cs.Should().Contain("Apperture_IntegrationTests");
        SqlServerTestConfiguration.LastResolutionSource.Should().Be(SqlServerTestConfiguration.ExplicitConnectionStringVariable);
    }

    [Fact]
    public void GetConnectionString_uses_default_connection_when_explicit_missing()
    {
        using var _ = new EnvScope(_saved);
        Set(
            ExplicitCs: null,
            Default: "Server=127.0.0.1,58361;User Id=sa;Password=aspire;TrustServerCertificate=True;Initial Catalog=Apperture;",
            Password: "legacy-pass");

        var cs = SqlServerTestConfiguration.GetConnectionString();

        cs.Should().NotBeNull();
        cs.Should().Contain("58361");
        cs.Should().Contain("Apperture_IntegrationTests");
        SqlServerTestConfiguration.LastResolutionSource.Should().Be(SqlServerTestConfiguration.DefaultConnectionStringVariable);
    }

    [Fact]
    public void GetConnectionString_falls_back_to_legacy_localhost_1433()
    {
        using var _ = new EnvScope(_saved);
        Set(ExplicitCs: null, Default: null, Password: "legacy-pass");

        var cs = SqlServerTestConfiguration.GetConnectionString();

        cs.Should().NotBeNull();
        cs.Should().Contain("localhost,1433");
        SqlServerTestConfiguration.LastResolutionSource.Should().StartWith("legacy MSSQL_SA_PASSWORD");
        SqlServerTestConfiguration.DescribeActiveConfiguration().Should().Contain("legacy MSSQL_SA_PASSWORD");
    }

    [Fact]
    public void GetGovernanceConnectionString_prefers_APPERTURE_TEST_SQL()
    {
        using var _ = new EnvScope(_saved);
        Set(
            Governance: "Server=ci,1433;User Id=sa;Password=x;TrustServerCertificate=True;Initial Catalog=Apperture_ManagementGovernanceTests;",
            ExplicitCs: "Server=127.0.0.1,58361;User Id=sa;Password=aspire;TrustServerCertificate=True;Initial Catalog=Apperture;",
            Default: null,
            Password: "legacy-pass");

        var cs = SqlServerTestConfiguration.GetGovernanceConnectionString();

        cs.Should().Be("Server=ci,1433;User Id=sa;Password=x;TrustServerCertificate=True;Initial Catalog=Apperture_ManagementGovernanceTests;");
        SqlServerTestConfiguration.LastResolutionSource.Should().Be(SqlServerTestConfiguration.GovernanceConnectionStringVariable);
    }

    [Fact]
    public void GetConnectionString_loads_explicit_from_env_file_with_semicolon_heavy_value()
    {
        using var _ = new EnvScope(_saved);
        Set(ExplicitCs: null, Default: null, Password: null, Governance: null);

        var dir = Path.Combine(Path.GetTempPath(), "apperture-env-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "AGENTS.md"), "# test repo root\n");
        var envFile = Path.Combine(dir, ".env");
        File.WriteAllText(
            envFile,
            """
            MSSQL_SA_PASSWORD=legacy-should-not-win
            APPERTURE_TEST_SQL_CONNECTION_STRING=Server=127.0.0.1,58361;User Id=sa;Password=secret;TrustServerCertificate=True;Initial Catalog=Apperture;
            """);

        SqlServerTestConfiguration.SetEnvFilePathOverrideForTests(envFile);
        SqlServerTestConfiguration.ResetEnvLoadForTests();

        try
        {
            var cs = SqlServerTestConfiguration.GetConnectionString();

            cs.Should().NotBeNull();
            cs.Should().Contain("58361");
            cs.Should().Contain("Apperture_IntegrationTests");
            SqlServerTestConfiguration.LastResolutionSource.Should().Be(SqlServerTestConfiguration.ExplicitConnectionStringVariable);
            SqlServerTestConfiguration.LastResolvedDataSource.Should().Be("127.0.0.1,58361");
        }
        finally
        {
            SqlServerTestConfiguration.SetEnvFilePathOverrideForTests(null);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("APPERTURE_TEST_SQL_CONNECTION_STRING=\"Server=127.0.0.1,58361;User Id=sa;Password=secret;TrustServerCertificate=True;\"", "127.0.0.1,58361")]
    [InlineData("APPERTURE_TEST_SQL_CONNECTION_STRING='Server=127.0.0.1,58361;User Id=sa;Password=secret;TrustServerCertificate=True;'", "127.0.0.1,58361")]
    [InlineData("APPERTURE_TEST_SQL_CONNECTION_STRING=Server=127.0.0.1,58361;User Id=sa;Password=secret;TrustServerCertificate=True; # aspire", "127.0.0.1,58361")]
    public void TryParseEnvLine_parses_connection_strings_with_semicolons_commas_and_quotes(string line, string expectedDataSourceFragment)
    {
        SqlServerTestConfiguration.TryParseEnvLine(line, out var key, out var value).Should().BeTrue();
        key.Should().Be(SqlServerTestConfiguration.ExplicitConnectionStringVariable);
        value.Should().NotBeNull();
        value.Should().Contain(expectedDataSourceFragment);
        value.Should().Contain("Password=secret");
    }

    [Fact]
    public void DescribeConnectionSummary_never_includes_password()
    {
        const string cs = "Server=127.0.0.1,58361;User Id=sa;Password=super-secret;TrustServerCertificate=True;Initial Catalog=Apperture_IntegrationTests;";
        var summary = SqlServerTestConfiguration.DescribeConnectionSummary(cs);

        summary.Should().Contain("58361");
        summary.Should().Contain("Apperture_IntegrationTests");
        summary.Should().Contain("sql auth (user sa)");
        summary.Should().NotContain("super-secret");
        summary.Should().NotContain("Password=");
    }

    // ---- disposable-target separation (the development SQL port and the development catalog) ----------------------------
    // None of these may reach a real server, even while the guard is being weakened on purpose: every address is unroutable
    // (203.0.113.0/24 is reserved for documentation), the port is what the guard recognises, and every connection string carries
    // Connect Timeout=1. The live development SQL server on 127.0.0.1,14333 is never named in a case that could connect.

    private const string Secret = "S3cret!Pw-Marker";
    private const string DevelopmentSql =
        "Server=203.0.113.8,14333;User Id=sa;Password=" + Secret + ";TrustServerCertificate=True;Connect Timeout=1;Initial Catalog=Apperture;";
    private const string UnroutableTestSql =
        "Server=203.0.113.7,1433;User Id=sa;Password=" + Secret + ";TrustServerCertificate=True;Connect Timeout=1;Initial Catalog=Apperture;";

    [Fact]
    public void GetConnectionString_refuses_the_development_sql_port_from_the_explicit_variable()
    {
        using var _ = new EnvScope(_saved);
        Set(ExplicitCs: DevelopmentSql, Default: null, Password: null);

        var act = () => SqlServerTestConfiguration.GetConnectionString();

        var message = act.Should().Throw<SqlTestTargetRefusedException>().Which.Message;
        message.Should().Contain("203.0.113.8,14333").And.Contain("reserved for the development SQL server").And.NotContain(Secret);
    }

    [Fact]
    public void GetConnectionString_refuses_an_unparseable_value_instead_of_failing_inside_the_parser()
    {
        using var _ = new EnvScope(_saved);
        Set(ExplicitCs: "this is not a connection string", Default: null, Password: null);

        var act = () => SqlServerTestConfiguration.GetConnectionString();

        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().Contain("unparseable");
    }

    [Fact]
    public void GetConnectionString_refuses_the_development_sql_port_when_it_arrives_as_the_aspire_default_connection()
    {
        using var _ = new EnvScope(_saved);
        Set(ExplicitCs: null, Default: DevelopmentSql, Password: null);

        var act = () => SqlServerTestConfiguration.GetConnectionString();

        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().Contain("14333");
    }

    [Fact]
    public void GetConnectionString_refuses_the_development_sql_port_from_the_legacy_server_variable()
    {
        using var _ = new EnvScope(_saved);
        Set(ExplicitCs: null, Default: null, Password: "legacy-pass");
        Environment.SetEnvironmentVariable("MSSQL_TEST_SERVER", "tcp:203.0.113.8,14333");

        var act = () => SqlServerTestConfiguration.GetConnectionString();

        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().Contain("14333");
    }

    [Fact]
    public void An_unreachable_test_server_never_falls_through_to_the_development_sql_port()
    {
        using var _ = new EnvScope(_saved);
        SqlServerTestConfiguration.SetSkipReachabilityProbeForTests(false);
        Set(ExplicitCs: UnroutableTestSql, Default: DevelopmentSql, Password: null);

        // The explicit target fails its 3 s probe; the next candidate is the development server and must be refused, not used.
        var act = () => SqlServerTestConfiguration.RequireConnectionString();

        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().Contain("14333");
    }

    [Fact]
    public void The_explicit_opt_in_lets_the_development_sql_port_through_the_resolver()
    {
        using var _ = new EnvScope(_saved);
        Set(ExplicitCs: DevelopmentSql, Default: null, Password: null);
        Environment.SetEnvironmentVariable(SqlTestTargetGuard.ProtectedTargetOptInVariable, "1");

        var cs = SqlServerTestConfiguration.GetConnectionString();

        cs.Should().NotBeNull().And.Contain("14333").And.Contain("Apperture_IntegrationTests");
    }

    [Theory]
    [InlineData("true")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    public void Only_the_exact_value_1_counts_as_the_opt_in(string value)
    {
        using var _ = new EnvScope(_saved);
        Set(ExplicitCs: DevelopmentSql, Default: null, Password: null);
        Environment.SetEnvironmentVariable(SqlTestTargetGuard.ProtectedTargetOptInVariable, value);

        var act = () => SqlServerTestConfiguration.GetConnectionString();

        act.Should().Throw<SqlTestTargetRefusedException>();
    }

    [Fact]
    public void The_opt_in_is_read_from_the_process_environment_and_not_from_the_env_file()
    {
        using var _ = new EnvScope(_saved);
        Set(ExplicitCs: null, Default: null, Password: null);
        var envFile = Path.Combine(Path.GetTempPath(), "apperture-env-optin-" + Guid.NewGuid().ToString("N") + ".env");
        File.WriteAllText(
            envFile,
            $"APPERTURE_TEST_SQL_CONNECTION_STRING={DevelopmentSql}\n{SqlTestTargetGuard.ProtectedTargetOptInVariable}=1\n");
        SqlServerTestConfiguration.SetEnvFilePathOverrideForTests(envFile);
        SqlServerTestConfiguration.ResetEnvLoadForTests();

        try
        {
            var act = () => SqlServerTestConfiguration.GetConnectionString();

            act.Should().Throw<SqlTestTargetRefusedException>();
        }
        finally
        {
            File.Delete(envFile);
        }
    }

    [Theory]
    [InlineData("Apperture")]
    [InlineData("Apperture_ManagementGovernance")]
    [InlineData("master")]
    public void GetGovernanceConnectionString_refuses_a_catalog_that_does_not_end_in_Tests(string catalog)
    {
        using var _ = new EnvScope(_saved);
        Set(
            Governance: $"Server=ci,1433;User Id=sa;Password={Secret};TrustServerCertificate=True;Initial Catalog={catalog};",
            ExplicitCs: null,
            Default: null,
            Password: null);

        var act = () => SqlServerTestConfiguration.GetGovernanceConnectionString();

        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().Contain($"database={catalog}").And.NotContain(Secret);
    }

    [Fact]
    public void GetGovernanceConnectionString_refuses_the_development_sql_port_in_the_ci_variable()
    {
        using var _ = new EnvScope(_saved);
        Set(
            Governance: "Server=203.0.113.8,14333;User Id=sa;Password=x;TrustServerCertificate=True;Initial Catalog=Apperture_ManagementGovernanceTests;",
            ExplicitCs: null,
            Default: null,
            Password: null);

        var act = () => SqlServerTestConfiguration.GetGovernanceConnectionString();

        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().Contain("14333");
    }

    [Fact]
    public void EnsureTestDatabaseExists_refuses_the_development_sql_port_before_it_connects()
    {
        using var _ = new EnvScope(_saved);
        var connectionString =
            $"Server=203.0.113.7,14333;User Id=sa;Password={Secret};TrustServerCertificate=True;Connect Timeout=1;Initial Catalog=Apperture_Probe;";

        var act = () => SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        // Had the guard not run first, this would have failed as an InvalidOperationException from the unreachable connect.
        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().Contain("14333");
    }

    [Fact]
    public void EnsureTestDatabaseExists_refuses_the_bare_development_catalog_before_it_connects()
    {
        using var _ = new EnvScope(_saved);

        var act = () => SqlServerTestConfiguration.EnsureTestDatabaseExists(UnroutableTestSql);

        act.Should().Throw<SqlTestTargetRefusedException>().Which.Message.Should().Contain("database=Apperture]");
    }

    [Fact]
    public async Task A_soft_skipping_scratch_helper_does_not_swallow_a_refused_target()
    {
        using var _ = new EnvScope(_saved);
        Set(ExplicitCs: DevelopmentSql, Default: null, Password: null);

        // CreateAsync catches InvalidOperationException (an unreachable server) and returns null so the caller can skip.
        var act = () => SqlServerScratchDatabase.CreateAsync("Apperture_Probe");

        await act.Should().ThrowAsync<SqlTestTargetRefusedException>();
    }

    [Theory]
    [InlineData("Forbidden_")]
    [InlineData("Apperture")]
    [InlineData("Apperture-")]
    [InlineData("Apperture_x]; DROP DATABASE master; --")]
    public async Task A_soft_skipping_scratch_helper_does_not_swallow_a_refused_catalog_on_an_accepted_server(string catalogPrefix)
    {
        using var _ = new EnvScope(_saved);
        // The server is accepted (a test port, not the development one) but unreachable, so the resolver hands it back and only the
        // catalog rule inside CreateAsync's try block can refuse. That refusal must not be mistaken for the unreachable-server
        // soft skip (null) by a catch that is wider than InvalidOperationException.
        Set(ExplicitCs: UnroutableTestSql, Default: null, Password: null);

        var act = () => SqlServerScratchDatabase.CreateAsync(catalogPrefix);

        await act.Should().ThrowAsync<SqlTestTargetRefusedException>();
    }

    [Fact]
    public async Task A_soft_skipping_scratch_helper_still_reports_an_unreachable_accepted_server_as_not_created()
    {
        using var _ = new EnvScope(_saved);
        Set(ExplicitCs: UnroutableTestSql, Default: null, Password: null);

        // Positive control for the test above: with a valid prefix the same unreachable server gives the soft-skip null, so the
        // refusal there came from the catalog rule and not from the server.
        var created = await SqlServerScratchDatabase.CreateAsync("Apperture_ScratchProbe");

        created.Should().BeNull();
    }

    private static void Set(
        string? ExplicitCs = "",
        string? Default = "",
        string? Password = "",
        string? Governance = "")
    {
        Environment.SetEnvironmentVariable(SqlServerTestConfiguration.ExplicitConnectionStringVariable, ExplicitCs);
        Environment.SetEnvironmentVariable(SqlServerTestConfiguration.DefaultConnectionStringVariable, Default);
        Environment.SetEnvironmentVariable(SqlServerTestConfiguration.GovernanceConnectionStringVariable, Governance);
        Environment.SetEnvironmentVariable("MSSQL_SA_PASSWORD", Password);
        Environment.SetEnvironmentVariable("MSSQL_TEST_SERVER", null);
    }

    private sealed class EnvScope : IDisposable
    {
        private readonly Dictionary<string, string?> _saved;
        private readonly string _isolatedEnvFile;

        public EnvScope(Dictionary<string, string?> saved)
        {
            _saved = saved;
            _isolatedEnvFile = Path.Combine(Path.GetTempPath(), "apperture-env-isolate-" + Guid.NewGuid().ToString("N") + ".env");
            File.WriteAllText(_isolatedEnvFile, string.Empty);
            SqlServerTestConfiguration.SetEnvFilePathOverrideForTests(_isolatedEnvFile);
            SqlServerTestConfiguration.ResetEnvLoadForTests();
            SqlServerTestConfiguration.SetSkipReachabilityProbeForTests(true);

            Save(SqlServerTestConfiguration.ExplicitConnectionStringVariable);
            Save(SqlServerTestConfiguration.DefaultConnectionStringVariable);
            Save(SqlServerTestConfiguration.GovernanceConnectionStringVariable);
            Save("MSSQL_SA_PASSWORD");
            Save("MSSQL_TEST_SERVER");
            Save("MSSQL_TEST_DATABASE");
            Save(SqlTestTargetGuard.ProtectedTargetOptInVariable);
            Environment.SetEnvironmentVariable(SqlTestTargetGuard.ProtectedTargetOptInVariable, null);
        }

        private void Save(string key) => _saved[key] = Environment.GetEnvironmentVariable(key);

        public void Dispose()
        {
            foreach (var (key, value) in _saved)
                Environment.SetEnvironmentVariable(key, value);
            SqlServerTestConfiguration.SetEnvFilePathOverrideForTests(null);
            SqlServerTestConfiguration.ResetEnvLoadForTests();
            SqlServerTestConfiguration.SetSkipReachabilityProbeForTests(false);
            if (File.Exists(_isolatedEnvFile))
                File.Delete(_isolatedEnvFile);
        }
    }
}
