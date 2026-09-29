using System.Runtime.InteropServices;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Providers;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Apperture.UnitTests.Secrets;

/// <summary>
/// Production SQL-secret registration stays fail-closed. A later composition may replace
/// ISecretProtector before hosted startup validation; there is no Linux/Testing/plaintext working default.
/// </summary>
public sealed class SqlSecretsRegistrationTests
{
    [Fact]
    public void AddAppertureSqlSecrets_completes_registration_without_an_explicit_protector()
    {
        var services = new ServiceCollection();
        var act = () => services.AddAppertureSqlSecrets();
        act.Should().NotThrow("WebApplicationFactory can only replace ISecretProtector after Program.cs registers it");
        services.Should().Contain(d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public async Task Unsupported_default_fails_hosted_startup_not_registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppertureSqlSecrets();
        await using var provider = services.BuildServiceProvider();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            provider.GetRequiredService<ISecretProtector>().Should().BeOfType<DpapiSecretProtector>();
            await StartSecretHostedServicesAsync(provider);
            return;
        }

        var act = () => StartSecretHostedServicesAsync(provider);
        await act.Should().ThrowAsync<PlatformNotSupportedException>()
            .WithMessage("*DPAPI*protector*Windows*");
    }

    [Fact]
    public async Task Explicit_protector_passes_hosted_startup_on_any_os()
    {
        var protector = new RecordingProtector();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppertureSqlSecrets(protector: protector);
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISecretProtector>().Should().BeSameAs(protector);
        await StartSecretHostedServicesAsync(provider);
    }

    [Fact]
    public async Task Replacement_after_registration_passes_hosted_startup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppertureSqlSecrets();
        services.RemoveAll<ISecretProtector>();
        services.AddSingleton<ISecretProtector>(new RecordingProtector());
        await using var provider = services.BuildServiceProvider();

        await StartSecretHostedServicesAsync(provider);
    }

    [Fact]
    public void Production_registration_has_no_linux_testing_or_plaintext_fallback()
    {
        var root = RepoRoot();
        var text = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Apperture.Persistence",
            "Apperture.Persistence.SqlServer",
            "ServiceCollectionExtensions.Secrets.cs"));

        text.Should().Contain("DpapiSecretProtector");
        text.Should().Contain("PlatformNotSupportedException");
        text.Should().Contain("IsOSPlatform(OSPlatform.Windows)");
        text.Should().Contain("AddHostedService<SecretProtectorStartupValidationHostedService>");
        text.Should().NotContain("plaintext");
        text.Should().NotContain("IsEnvironment");
        text.Should().NotContain("Testing");
        text.Should().NotContain("APPERTURE_BROWSER");
        text.Should().NotContain("BrowserHarness");
        text.Should().NotContain("DisableEncryption");
    }

    private static async Task StartSecretHostedServicesAsync(IServiceProvider provider)
    {
        var hosted = provider.GetServices<IHostedService>().ToList();
        hosted.Should().NotBeEmpty("AddAppertureSqlSecrets must register startup validation");
        foreach (var service in hosted)
            await service.StartAsync(CancellationToken.None);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return dir!.FullName;
    }

    private sealed class RecordingProtector : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(plaintext.ToArray());

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(protectedPayload.ToArray());
    }
}
