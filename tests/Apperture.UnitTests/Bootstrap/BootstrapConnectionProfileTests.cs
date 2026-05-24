using Apperture.Bootstrap;
using Apperture.Bootstrap.Stubs;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.Bootstrap;

public sealed class BootstrapConnectionProfileTests
{
    [Fact]
    public void ConnectionString_prefers_DefaultConnection_over_SqlServer_section_in_Development()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=aspire-sql,1433;Database=Apperture;User Id=sa;Password=aspire;TrustServerCertificate=True",
                ["SqlServer:Server"] = "localhost,1433",
                ["SqlServer:Database"] = "Apperture",
                ["SqlServer:User"] = "sa",
                ["SqlServer:Password"] = "docker-compose",
            })
            .Build();

        var profile = CreateProfile(config);

        profile.ConnectionString.Should().Contain("aspire-sql");
        profile.ConnectionString.Should().NotContain("docker-compose");
    }

    [Fact]
    public void ConnectionString_uses_SqlServer_section_when_DefaultConnection_missing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SqlServer:Server"] = "localhost,1433",
                ["SqlServer:Database"] = "Apperture",
                ["SqlServer:User"] = "sa",
                ["SqlServer:Password"] = "from-config",
            })
            .Build();

        var profile = CreateProfile(config);

        profile.ConnectionString.Should().Contain("localhost,1433");
        profile.ConnectionString.Should().Contain("from-config");
    }

    private static BootstrapConnectionProfile CreateProfile(IConfiguration configuration)
    {
        var sqlConfig = new BootstrapSqlConfig();
        configuration.GetSection(BootstrapSqlConfig.SectionName).Bind(sqlConfig);

        return new BootstrapConnectionProfile(
            Options.Create(sqlConfig),
            configuration,
            new StubBootstrapCredentialResolver(),
            new TestHostEnvironment(),
            NullLogger<BootstrapConnectionProfile>.Instance);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Apperture.UnitTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
