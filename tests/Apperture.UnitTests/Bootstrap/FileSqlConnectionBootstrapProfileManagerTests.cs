using Apperture.Bootstrap;
using Apperture.Bootstrap.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Bootstrap;

public sealed class FileSqlConnectionBootstrapProfileManagerTests : IDisposable
{
    private readonly string _configPath;
    private readonly string? _previousEnvPath;

    public FileSqlConnectionBootstrapProfileManagerTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "apperture-bootstrap-profile-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "configuration.json");
        _previousEnvPath = Environment.GetEnvironmentVariable(BootstrapConfigurationStoreOptions.ConfigPathEnvironmentVariableName);
        Environment.SetEnvironmentVariable(BootstrapConfigurationStoreOptions.ConfigPathEnvironmentVariableName, _configPath);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(
            BootstrapConfigurationStoreOptions.ConfigPathEnvironmentVariableName,
            _previousEnvPath);

        var dir = Path.GetDirectoryName(_configPath);
        if (dir is not null && Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task UpdateProfileAsync_createsConfigurationFile()
    {
        var store = new FileBootstrapConfigurationStore(_configPath);
        var manager = new FileSqlConnectionBootstrapProfileManager(store);
        var profile = new SqlConnectionBootstrapProfile
        {
            Server = "prod-sql",
            Database = "Apperture",
            AuthMode = BootstrapAuthMode.WindowsIntegrated
        };

        await manager.UpdateProfileAsync(profile);

        File.Exists(_configPath).Should().BeTrue();
        var loaded = await manager.GetProfileAsync();
        loaded.Should().NotBeNull();
        loaded!.Server.Should().Be("prod-sql");
    }
}
