using Apperture.Bootstrap;
using Apperture.Bootstrap.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Bootstrap;

public sealed class FileBootstrapConfigurationStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string? _previousEnvPath;

    public FileBootstrapConfigurationStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "apperture-bootstrap-tests", Guid.NewGuid().ToString("N"));
        _previousEnvPath = Environment.GetEnvironmentVariable(BootstrapConfigurationStoreOptions.ConfigPathEnvironmentVariableName);
        Environment.SetEnvironmentVariable(BootstrapConfigurationStoreOptions.ConfigPathEnvironmentVariableName, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(
            BootstrapConfigurationStoreOptions.ConfigPathEnvironmentVariableName,
            _previousEnvPath);

        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetAsync_missingFile_returnsNull()
    {
        var store = CreateStore("missing", "configuration.json");

        var doc = await store.GetAsync();

        doc.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_then_GetAsync_roundTrips()
    {
        var store = CreateStore("roundtrip", "configuration.json");
        var original = new BootstrapConfigurationDocument
        {
            Profile = new SqlConnectionBootstrapProfile
            {
                Server = "sql.example.com",
                Database = "Apperture",
                AuthMode = BootstrapAuthMode.WindowsIntegrated
            },
            CredentialReference = new BootstrapCredentialReference
            {
                SourceType = BootstrapCredentialSourceType.WindowsIntegrated
            }
        };

        await store.SaveAsync(original);
        var loaded = await store.GetAsync();

        loaded.Should().NotBeNull();
        loaded!.Profile.Server.Should().Be("sql.example.com");
        loaded.Profile.Database.Should().Be("Apperture");
        loaded.Profile.AuthMode.Should().Be(BootstrapAuthMode.WindowsIntegrated);
        loaded.CredentialReference!.SourceType.Should().Be(BootstrapCredentialSourceType.WindowsIntegrated);
        loaded.Version.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SaveAsync_createsParentDirectories()
    {
        var store = CreateStore("nested", "deep", "configuration.json");

        await store.SaveAsync(CreateMinimalDocument());

        var path = Path.Combine(_tempRoot, "nested", "deep", "configuration.json");
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_malformedJson_throwsWithPath()
    {
        var path = Path.Combine(_tempRoot, "bad", "configuration.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not valid json");

        var store = new FileBootstrapConfigurationStore(path);

        var act = async () => await store.GetAsync();

        var ex = await act.Should().ThrowAsync<BootstrapConfigurationPersistenceException>();
        ex.Which.FilePath.Should().Be(path);
        ex.Which.Message.Should().Contain(path);
    }

    [Fact]
    public async Task SaveAsync_doesNotWritePasswordFields()
    {
        var store = CreateStore("secrets", "configuration.json");

        await store.SaveAsync(CreateMinimalDocument());

        var path = Path.Combine(_tempRoot, "secrets", "configuration.json");
        var json = await File.ReadAllTextAsync(path);
        json.Should().NotContain("\"Password\"");
        json.Should().NotContain("\"User\"");
        json.Should().Contain(Environment.NewLine); // indented for operator readability
    }

    [Fact]
    public void PathResolver_honorsEnvironmentOverride()
    {
        var expected = Path.Combine(_tempRoot, "env-override.json");
        Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
        Environment.SetEnvironmentVariable(BootstrapConfigurationStoreOptions.ConfigPathEnvironmentVariableName, expected);

        var resolved = BootstrapConfigurationStorePathResolver.Resolve();

        resolved.Should().Be(Path.GetFullPath(expected));
    }

    private FileBootstrapConfigurationStore CreateStore(params string[] pathParts)
    {
        var path = Path.Combine(_tempRoot, Path.Combine(pathParts));
        return new FileBootstrapConfigurationStore(path);
    }

    private static BootstrapConfigurationDocument CreateMinimalDocument() =>
        new()
        {
            Profile = new SqlConnectionBootstrapProfile
            {
                Server = "localhost",
                Database = "Apperture",
                AuthMode = BootstrapAuthMode.SqlPassword
            }
        };
}
