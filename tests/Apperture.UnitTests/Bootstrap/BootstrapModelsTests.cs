using Apperture.Bootstrap.Models;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace Apperture.UnitTests.Bootstrap;

/// <summary>
/// Model validity and round-trip serialization for bootstrap models.
/// </summary>
public sealed class BootstrapModelsTests
{
    [Fact]
    public void BootstrapAuthMode_has_expected_values()
    {
        Enum.GetValues<BootstrapAuthMode>().Should().Contain(BootstrapAuthMode.ManagedIdentity);
        Enum.GetValues<BootstrapAuthMode>().Should().Contain(BootstrapAuthMode.WindowsIntegrated);
        Enum.GetValues<BootstrapAuthMode>().Should().Contain(BootstrapAuthMode.SqlPassword);
        Enum.GetValues<BootstrapAuthMode>().Should().Contain(BootstrapAuthMode.CertificateBased);
        Enum.GetValues<BootstrapAuthMode>().Should().Contain(BootstrapAuthMode.Development);
    }

    [Fact]
    public void BootstrapCredentialSourceType_has_expected_values()
    {
        Enum.GetValues<BootstrapCredentialSourceType>().Should().Contain(BootstrapCredentialSourceType.ManagedIdentity);
        Enum.GetValues<BootstrapCredentialSourceType>().Should().Contain(BootstrapCredentialSourceType.KeyVaultReference);
        Enum.GetValues<BootstrapCredentialSourceType>().Should().Contain(BootstrapCredentialSourceType.DpapiProtectedLocal);
    }

    [Fact]
    public void BootstrapConfigurationDocument_roundtrip_serialization()
    {
        var doc = new BootstrapConfigurationDocument
        {
            Profile = new SqlConnectionBootstrapProfile
            {
                Server = "localhost",
                Database = "Apperture",
                AuthMode = BootstrapAuthMode.WindowsIntegrated,
                TrustServerCertificate = true
            },
            CredentialReference = new BootstrapCredentialReference
            {
                SourceType = BootstrapCredentialSourceType.WindowsIntegrated,
                Reference = null
            },
            Version = "v1"
        };

        var json = JsonSerializer.Serialize(doc);
        var deserialized = JsonSerializer.Deserialize<BootstrapConfigurationDocument>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Profile.Should().NotBeNull();
        deserialized.Profile!.Server.Should().Be("localhost");
        deserialized.Profile.Database.Should().Be("Apperture");
        deserialized.Profile.AuthMode.Should().Be(BootstrapAuthMode.WindowsIntegrated);
        deserialized.CredentialReference!.SourceType.Should().Be(BootstrapCredentialSourceType.WindowsIntegrated);
        deserialized.Version.Should().Be("v1");
    }

    [Fact]
    public void BootstrapValidationResult_roundtrip_serialization()
    {
        var result = new BootstrapValidationResult { Success = false, Errors = ["Server required", "Database required"] };
        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<BootstrapValidationResult>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
        deserialized.Errors.Should().HaveCount(2).And.Contain("Server required");
    }
}
