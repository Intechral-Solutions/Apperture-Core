using Apperture.Bootstrap;
using Apperture.Bootstrap.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Bootstrap;

public sealed class FileStoreBootstrapCredentialManagerTests : IDisposable
{
    private readonly string _configPath;

    public FileStoreBootstrapCredentialManagerTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "apperture-bootstrap-cred-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "configuration.json");
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (dir is not null && Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task UpdateAsync_withSensitivePayload_throwsWithoutWritingFile()
    {
        var store = new FileBootstrapConfigurationStore(_configPath);
        var manager = new FileStoreBootstrapCredentialManager(store);

        var act = async () => await manager.UpdateAsync(new BootstrapUpdateRequest
        {
            SensitivePayload = "SuperSecretPassword123!"
        });

        var ex = await act.Should().ThrowAsync<BootstrapConfigurationPersistenceException>();
        ex.Which.Message.Should().Contain("Epic 108b");
        File.Exists(_configPath).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_withCredentialReference_persistsReferenceNotPlaintext()
    {
        var store = new FileBootstrapConfigurationStore(_configPath);
        var manager = new FileStoreBootstrapCredentialManager(store);

        await manager.UpdateAsync(new BootstrapUpdateRequest
        {
            CredentialReference = new BootstrapCredentialReference
            {
                SourceType = BootstrapCredentialSourceType.KeyVaultReference,
                Reference = "https://vault.example.net/secrets/sql-password/version"
            }
        });

        var json = await File.ReadAllTextAsync(_configPath);
        json.Should().Contain("KeyVaultReference");
        json.Should().Contain("vault.example.net");
        json.Should().NotContain("\"Password\"");
        json.Should().NotContain("SuperSecret");
    }
}
