using Apperture.Bootstrap;
using Apperture.Bootstrap.Models;
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

    [Fact]
    public void ConnectionString_uses_file_store_profile_when_DefaultConnection_missing()
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

        var fileDoc = new BootstrapConfigurationDocument
        {
            Profile = new SqlConnectionBootstrapProfile
            {
                Server = "file-sql",
                Database = "Apperture",
                AuthMode = BootstrapAuthMode.WindowsIntegrated
            }
        };

        var profile = CreateProfile(config, new FixedBootstrapConfigurationStore(fileDoc));

        profile.ConnectionString.Should().Contain("file-sql");
        profile.ConnectionString.Should().NotContain("from-config");
    }

    private static BootstrapConnectionProfile CreateProfile(
        IConfiguration configuration,
        IBootstrapConfigurationStore? configurationStore = null)
    {
        var sqlConfig = new BootstrapSqlConfig();
        configuration.GetSection(BootstrapSqlConfig.SectionName).Bind(sqlConfig);

        return new BootstrapConnectionProfile(
            Options.Create(sqlConfig),
            configuration,
            configurationStore ?? new StubBootstrapConfigurationStore(),
            new StubBootstrapCredentialResolver(),
            new TestHostEnvironment(),
            NullLogger<BootstrapConnectionProfile>.Instance);
    }

    private sealed class FixedBootstrapConfigurationStore : IBootstrapConfigurationStore
    {
        private readonly BootstrapConfigurationDocument _document;

        public FixedBootstrapConfigurationStore(BootstrapConfigurationDocument document) =>
            _document = document;

        public Task<BootstrapConfigurationDocument?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<BootstrapConfigurationDocument?>(_document);

        public Task SaveAsync(BootstrapConfigurationDocument document, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Apperture.UnitTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
