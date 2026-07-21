using FluentAssertions;
using Xunit;

namespace Apperture.IntegrationTests;

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
