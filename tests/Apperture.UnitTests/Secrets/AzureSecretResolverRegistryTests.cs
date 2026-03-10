using Apperture.Core.Security.Secrets.Models;
using Apperture.RuntimeHost.Azure.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.Secrets;

/// <summary>
/// Unit tests for Azure secret resolver registry (routing by SecretRef format; no Key Vault calls).
/// </summary>
public sealed class AzureSecretResolverRegistryTests
{
    private static AzureSecretResolverRegistry CreateRegistry()
    {
        var options = new AzureKeyVaultOptions { VaultUri = new Uri("https://test-vault.vault.azure.net/") };
        var client = new SecretClient(options.VaultUri, new DefaultAzureCredential());
        var resolver = new AzureKeyVaultSecretResolver(client, new DefaultAzureCredential(), Options.Create(options));
        return new AzureSecretResolverRegistry(resolver);
    }

    [Fact]
    public void GetResolverFor_ReturnsResolverForKvPrefixed()
    {
        var registry = CreateRegistry();
        var secretRef = new SecretRef("kv:api-key");

        var result = registry.GetResolverFor(secretRef);

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetResolverFor_ReturnsResolverForKeyVaultUri()
    {
        var registry = CreateRegistry();
        var secretRef = new SecretRef("https://myvault.vault.azure.net/secrets/my-secret");

        var result = registry.GetResolverFor(secretRef);

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetResolverFor_ReturnsNullForGuidValue()
    {
        var registry = CreateRegistry();
        var secretRef = new SecretRef("550e8400-e29b-41d4-a716-446655440000");

        var result = registry.GetResolverFor(secretRef);

        result.Should().BeNull();
    }

    [Fact]
    public void GetResolverFor_ReturnsNullForArbitraryString()
    {
        var registry = CreateRegistry();
        var secretRef = new SecretRef("some-other-format");

        var result = registry.GetResolverFor(secretRef);

        result.Should().BeNull();
    }
}
