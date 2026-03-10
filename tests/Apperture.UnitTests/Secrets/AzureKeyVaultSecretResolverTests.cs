using Apperture.Core.Security.Secrets.Models;
using Apperture.RuntimeHost.Azure.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.Secrets;

/// <summary>
/// Unit tests for Azure Key Vault secret resolver (invalid ref and config; no real Key Vault calls).
/// </summary>
public sealed class AzureKeyVaultSecretResolverTests
{
    private static AzureKeyVaultSecretResolver CreateResolver(AzureKeyVaultOptions? options = null)
    {
        options ??= new AzureKeyVaultOptions { VaultUri = new Uri("https://test-vault.vault.azure.net/") };
        var client = new SecretClient(options.VaultUri!, new DefaultAzureCredential());
        return new AzureKeyVaultSecretResolver(client, new DefaultAzureCredential(), Options.Create(options));
    }

    [Fact]
    public async Task ResolveAsync_ThrowsForInvalidRef()
    {
        var resolver = CreateResolver();
        var invalidRef = new SecretRef("not-kv-and-not-uri");

        Func<Task> act = async () => _ = await resolver.ResolveAsync(invalidRef, default);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*kv:secret-name*Key Vault secret URI*");
    }

    [Fact]
    public async Task ResolveAsync_ThrowsForEmptyValue()
    {
        var resolver = CreateResolver();
        var invalidRef = new SecretRef("");

        Func<Task> act = async () => _ = await resolver.ResolveAsync(invalidRef, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ResolveAsync_RequiresVaultUri_WhenUsingKvPrefix()
    {
        var options = new AzureKeyVaultOptions { VaultUri = null };
        var client = new SecretClient(new Uri("https://fake.vault.azure.net/"), new DefaultAzureCredential());
        var resolver = new AzureKeyVaultSecretResolver(client, new DefaultAzureCredential(), Options.Create(options));
        var kvRef = new SecretRef("kv:my-secret");

        Func<Task> act = async () => _ = await resolver.ResolveAsync(kvRef, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*KeyVault:VaultUri*");
    }
}
